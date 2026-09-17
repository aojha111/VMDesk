namespace VMDesk.Application.Services;

public static class VmCredentialResolver
{
    public static string ResolveUsername(string credentialReference, string selectedCredentialUsername, string manualUsername)
    {
        if (!string.IsNullOrWhiteSpace(credentialReference) && !string.IsNullOrWhiteSpace(selectedCredentialUsername))
        {
            return selectedCredentialUsername;
        }

        return string.IsNullOrWhiteSpace(manualUsername) ? string.Empty : manualUsername.Trim();
    }
}
