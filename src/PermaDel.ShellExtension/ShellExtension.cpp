// PermaDel File Explorer extension.
// Adds "Shred with PermaDel" to the Windows 11 context menu with a submenu of overwrite pass counts. Invoking a
// pass count starts PermaDel.exe, which lives next to this DLL, and streams the selected paths to it over stdin.

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <algorithm>
#include <set>
#include <string>
#include <vector>

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InProc;
using Microsoft::WRL::Make;
using Microsoft::WRL::Module;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

namespace
{
    constexpr int MinPasses = 1;
    constexpr int MaxPasses = 35;
    constexpr int FallbackPasses = 3;
    constexpr int PassPresets[] = { 1, 3, 7, 35 };

    HMODULE g_module = nullptr;

    std::wstring ModuleDirectory()
    {
        std::wstring path(MAX_PATH, L'\0');
        DWORD length = 0;
        while ((length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()))) == path.size())
            path.resize(path.size() * 2);
        path.resize(length);
        return path.substr(0, path.find_last_of(L'\\') + 1);
    }

    // Shared with the app, which writes this value from its Settings page.
    int ReadDefaultPasses()
    {
        DWORD value = 0;
        DWORD size = sizeof(value);
        if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\PermaDel", L"DefaultPasses", RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS)
            return FallbackPasses;
        return std::clamp(static_cast<int>(value), MinPasses, MaxPasses);
    }

    std::wstring CollectPaths(IShellItemArray* items)
    {
        std::wstring paths;
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count)))
            return paths;

        for (DWORD i = 0; i < count; ++i)
        {
            ComPtr<IShellItem> item;
            PWSTR path = nullptr;
            if (SUCCEEDED(items->GetItemAt(i, &item)) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            {
                paths.append(path).push_back(L'\n');
                CoTaskMemFree(path);
            }
        }
        return paths;
    }

    // Starts PermaDel.exe --shred <passes> and writes the newline-separated UTF-16 paths to its stdin.
    // Only the pipe's read end is inherited, so no Explorer handles leak into the child process.
    HRESULT LaunchPermaDel(const std::wstring& paths, int passes)
    {
        SECURITY_ATTRIBUTES security{ sizeof(security), nullptr, TRUE };
        HANDLE readPipe = nullptr;
        HANDLE writePipe = nullptr;
        const auto bytes = static_cast<DWORD>(paths.size() * sizeof(wchar_t));
        if (!CreatePipe(&readPipe, &writePipe, &security, bytes))
            return HRESULT_FROM_WIN32(GetLastError());
        SetHandleInformation(writePipe, HANDLE_FLAG_INHERIT, 0);

        SIZE_T attributeSize = 0;
        InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeSize);
        std::vector<BYTE> attributeBuffer(attributeSize);
        const auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributeBuffer.data());
        InitializeProcThreadAttributeList(attributes, 1, 0, &attributeSize);
        UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, &readPipe, sizeof(readPipe), nullptr, nullptr);

        STARTUPINFOEXW startup{};
        startup.StartupInfo.cb = sizeof(startup);
        startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        startup.StartupInfo.hStdInput = readPipe;
        startup.lpAttributeList = attributes;

        const auto directory = ModuleDirectory();
        const auto executable = directory + L"PermaDel.exe";
        auto commandLine = L"\"" + executable + L"\" --shred " + std::to_wstring(passes);

        PROCESS_INFORMATION process{};
        const bool started = CreateProcessW(executable.c_str(), commandLine.data(), nullptr, nullptr, TRUE,
            EXTENDED_STARTUPINFO_PRESENT, nullptr, directory.c_str(), &startup.StartupInfo, &process);
        const HRESULT result = started ? S_OK : HRESULT_FROM_WIN32(GetLastError());

        DeleteProcThreadAttributeList(attributes);
        CloseHandle(readPipe);
        if (started)
        {
            AllowSetForegroundWindow(process.dwProcessId);
            DWORD written = 0;
            WriteFile(writePipe, paths.data(), bytes, &written, nullptr);
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
        CloseHandle(writePipe);
        return result;
    }
}

class CommandEnumerator final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
{
public:
    explicit CommandEnumerator(std::vector<ComPtr<IExplorerCommand>> commands) : m_commands(std::move(commands)) {}

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
    {
        ULONG copied = 0;
        for (; copied < count && m_position < m_commands.size(); ++copied, ++m_position)
            m_commands[m_position].CopyTo(&commands[copied]);
        if (fetched)
            *fetched = copied;
        return copied == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG count) override
    {
        m_position = std::min(m_position + count, m_commands.size());
        return S_OK;
    }

    IFACEMETHODIMP Reset() override
    {
        m_position = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override
    {
        *clone = nullptr;
        return E_NOTIMPL;
    }

private:
    std::vector<ComPtr<IExplorerCommand>> m_commands;
    size_t m_position = 0;
};

// The default-constructed instance is the top-level "Shred with PermaDel" command created by File Explorer;
// the other constructor builds its submenu entries.
class DECLSPEC_UUID("5B3E6A1C-7F2D-4C8E-9A41-2D6F8B0E3C97") ShredCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    ShredCommand() = default;

    ShredCommand(std::wstring title, int passes, EXPCMDFLAGS flags, EXPCMDSTATE state)
        : m_title(std::move(title)), m_passes(passes), m_flags(flags), m_state(state), m_isRoot(false) {}

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override
    {
        return SHStrDupW(m_title.c_str(), title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        *icon = nullptr;
        return m_isRoot ? SHStrDupW((ModuleDirectory() + L"PermaDel.exe,0").c_str(), icon) : E_NOTIMPL;
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tooltip) override
    {
        *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* name) override
    {
        *name = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        *state = m_state;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (m_passes == 0)
            return S_OK;
        const auto paths = CollectPaths(items);
        return paths.empty() ? S_OK : LaunchPermaDel(paths, m_passes);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = m_isRoot ? ECF_HASSUBCOMMANDS : m_flags;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        *commands = nullptr;
        if (!m_isRoot)
            return E_NOTIMPL;

        const int defaultPasses = ReadDefaultPasses();
        std::set<int> passCounts(std::begin(PassPresets), std::end(PassPresets));
        passCounts.insert(defaultPasses);

        std::vector<ComPtr<IExplorerCommand>> entries;
        for (const int passes : passCounts)
        {
            auto title = std::to_wstring(passes) + (passes == 1 ? L" pass" : L" passes");
            if (passes == defaultPasses)
                title += L" (default)";
            entries.push_back(Make<ShredCommand>(std::move(title), passes, ECF_DEFAULT, ECS_ENABLED));
        }
        entries.push_back(Make<ShredCommand>(L"", 0, ECF_ISSEPARATOR, ECS_ENABLED));
        entries.push_back(Make<ShredCommand>(L"Permanently destroys the selection - it can't be recovered", 0, ECF_DEFAULT, ECS_DISABLED));

        return Make<CommandEnumerator>(std::move(entries)).CopyTo(commands);
    }

private:
    std::wstring m_title = L"Shred with PermaDel";
    int m_passes = 0;
    EXPCMDFLAGS m_flags = ECF_DEFAULT;
    EXPCMDSTATE m_state = ECS_ENABLED;
    bool m_isRoot = true;
};

CoCreatableClass(ShredCommand)

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(_In_ REFCLSID clsid, _In_ REFIID iid, _Outptr_ LPVOID* instance)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, instance);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
