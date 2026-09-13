using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Security.Credentials.UI;

namespace PermaDel.Services;

/// <summary>
/// Confirms that the person at the PC is the signed-in Windows user: with Windows Hello (PIN, face or fingerprint) when
/// it's set up, and with the account password otherwise. An account without a password has nothing to verify.
/// </summary>
internal static class AccountVerification
{
    private const int ErrorLogonFailure = 1326;
    private const int ErrorAccountRestriction = 1327;
    private const int LogonInteractive = 2;
    private const int LogonProviderDefault = 0;
    private const uint CredPackProtectedCredentials = 0x1;
    private const uint CredUiWinEnumerateCurrentUser = 0x200;
    private const int MaxNameLength = 514;
    private const int MaxPasswordLength = 257;

    /// <param name="window">The window that owns the prompt.</param>
    /// <param name="message">What the user is approving.</param>
    /// <exception cref="COMException">Windows Hello reported an error.</exception>
    public static async Task<bool> VerifyAsync(nint window, string message)
    {
        if (await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available)
            return await UserConsentVerifierInterop.RequestVerificationForWindowAsync(window, message) == UserConsentVerificationResult.Verified;

        return !await Task.Run(HasPassword) || PromptForPassword(window, message);
    }

    /// <summary>
    /// Signing in with an empty password succeeds, or fails with ERROR_ACCOUNT_RESTRICTION where blank passwords are
    /// limited to console sign-in, only for an account that has no password.
    /// </summary>
    private static bool HasPassword()
    {
        if (LogonUserW(Environment.UserName, Environment.UserDomainName, ['\0'], LogonInteractive, LogonProviderDefault, out var token))
        {
            token.Dispose();
            return false;
        }
        return Marshal.GetLastPInvokeError() != ErrorAccountRestriction;
    }

    /// <summary>Shows the Windows Security prompt for the current user until the right password is entered or the prompt is cancelled.</summary>
    private static bool PromptForPassword(nint window, string message)
    {
        var info = new CredUiInfo { Size = Marshal.SizeOf<CredUiInfo>(), Parent = window, CaptionText = "PermaDel", MessageText = message };
        for (var error = 0; ; error = ErrorLogonFailure)
        {
            uint package = 0;
            if (CredUIPromptForWindowsCredentialsW(ref info, error, ref package, 0, 0, out var buffer, out var bufferSize, 0, CredUiWinEnumerateCurrentUser) != 0)
                return false;

            try
            {
                if (IsCurrentUser(buffer, bufferSize))
                    return true;
            }
            finally
            {
                Marshal.Copy(new byte[bufferSize], 0, buffer, (int)bufferSize);
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    private static bool IsCurrentUser(nint buffer, uint bufferSize)
    {
        var user = new char[MaxNameLength];
        var domain = new char[MaxNameLength];
        var password = new char[MaxPasswordLength];
        int userLength = user.Length, domainLength = domain.Length, passwordLength = password.Length;
        try
        {
            if (!CredUnPackAuthenticationBufferW(CredPackProtectedCredentials, buffer, bufferSize, user, ref userLength, domain, ref domainLength, password, ref passwordLength))
                return false;

            // The prompt returns the account as "DOMAIN\user", for example "MicrosoftAccount\name@example.com".
            var account = ReadString(user);
            var separator = account.IndexOf('\\');
            var accountDomain = separator < 0 ? ReadString(domain) : account[..separator];
            if (!LogonUserW(account[(separator + 1)..], accountDomain.Length == 0 ? null : accountDomain, password, LogonInteractive, LogonProviderDefault, out var token))
                return false;

            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var current = WindowsIdentity.GetCurrent())
                return identity.User == current.User;
        }
        finally
        {
            Array.Clear(password);
        }
    }

    private static string ReadString(char[] buffer)
    {
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int Size;
        public nint Parent;
        public string MessageText;
        public string CaptionText;
        public nint Banner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForWindowsCredentialsW(ref CredUiInfo uiInfo, int authError, ref uint authPackage, nint inAuthBuffer, uint inAuthBufferSize, out nint outAuthBuffer, out uint outAuthBufferSize, nint save, uint flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBufferW(uint flags, nint authBuffer, uint authBufferSize, [Out] char[] userName, ref int maxUserName, [Out] char[] domainName, ref int maxDomainName, [Out] char[] password, ref int maxPassword);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUserW(string userName, string? domain, char[] password, int logonType, int logonProvider, out SafeAccessTokenHandle token);
}
