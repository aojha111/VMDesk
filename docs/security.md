# Security

Passwords are accepted only by `PasswordBox` and sent to Windows Credential
Manager. SQLite stores a credential reference, never a password. Normal JSON
exports omit credential references and password values. Logging is redacted and
callers must not pass secrets, clipboard contents, or tokens to log methods.

The installer does not require administrator privileges for normal app use and
does not remove `%LOCALAPPDATA%\VMDesk` during an ordinary uninstall.