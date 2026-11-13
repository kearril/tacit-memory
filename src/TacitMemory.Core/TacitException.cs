namespace TacitMemory.Core;

/// <summary>Why a facade operation was refused.</summary>
public enum TacitError
{
    /// <summary>The memory directory has files in it but is not a Git repository.</summary>
    MemoryDirectoryNotEmpty,

    /// <summary>An option given to <c>init</c> differs from what the existing <c>config.yaml</c> says.</summary>
    ConflictsWithConfig,

    /// <summary><c>config.yaml</c> or the memory repository does not exist yet; <c>tacit init</c> creates them.</summary>
    NotInitialized,

    /// <summary><c>config.yaml</c> cannot be parsed or has wrong settings; the message names every key path.</summary>
    ConfigInvalid,

    /// <summary>A prompt document in <c>prompts/</c> breaks its limits or its format.</summary>
    PromptInvalid,

    /// <summary>The built-in text was asked for a target that has none.</summary>
    NoBuiltInText,

    /// <summary>A context file's resident block markers are incomplete or repeated.</summary>
    MarkersBroken,

    /// <summary>A file TacitMemory must read or edit is not valid UTF-8; it is left alone.</summary>
    FileNotUtf8,

    /// <summary><c>bindings.json</c> cannot be read.</summary>
    BindingsInvalid,

    /// <summary>The client's skill link cannot be made, or its name is taken by something that is not the memory skill's link.</summary>
    SkillLinkRefused,

    /// <summary>A client name is not 1 to 32 letters, digits or hyphens.</summary>
    ClientNameInvalid,

    /// <summary>The client name is already bound to other paths; the binding must be disconnected first.</summary>
    BindingConflict,

    /// <summary>No binding has the client name.</summary>
    BindingNotFound,

    /// <summary>The folder the context file goes into does not exist; <c>connect</c> does not create it.</summary>
    ContextFolderMissing,

    /// <summary>The skills folder does not exist; <c>connect</c> does not create it.</summary>
    SkillsFolderMissing,

    /// <summary>A <c>git</c> command failed.</summary>
    GitFailed,

    /// <summary>A backup would overwrite an existing file; a backup never overwrites one.</summary>
    BackupExists,

    /// <summary>A request names a value the operation does not accept, such as a relative backup directory.</summary>
    RequestInvalid,

    /// <summary>The cloud backup was asked for, but <c>backup.webdav</c> is not in <c>config.yaml</c>.</summary>
    CloudBackupNotConfigured,

    /// <summary>The cloud backup failed on the WebDAV service: connecting, authenticating, uploading or replacing the remote backup.</summary>
    CloudBackupFailed,

    /// <summary>A file or folder the operation needs could not be read or written, such as a backup directory that cannot be created.</summary>
    FileSystemFailed,

    /// <summary>The service failed in a way no operation expected; the log holds the details.</summary>
    InternalFailure,

    /// <summary>The backup zip cannot be restored: it has no readable manifest, its contents and manifest disagree, or an entry lies outside the allowed places.</summary>
    RestoreInvalid,

    /// <summary>A restore failed after it began. The memory repository is put back where it was, unless the message says the rollback failed too.</summary>
    RestoreFailed,

    /// <summary>The bundled sqlite-vec extension is missing or not the release TacitMemory was built with.</summary>
    VectorExtensionInvalid,

    /// <summary>The management API refused a request whose Origin is not the origin of the request itself (a browser page on another site).</summary>
    ApiOriginRefused,

    /// <summary>The management API refused a local request whose Host is not a loopback name (DNS rebinding).</summary>
    ApiHostRefused,

    /// <summary>The management API refused a POST or PUT whose Content-Type is not application/json.</summary>
    ApiContentTypeRefused,

    /// <summary>The management API refused a connection from another device that has no valid session.</summary>
    ApiUnauthorized,

    /// <summary>The MCP endpoint refused a connection from another device; MCP is for this machine only.</summary>
    McpRemoteRefused,

    /// <summary>The host refused a connection from another device to a path other than <c>/</c>, <c>/ui</c> and <c>/api</c>.</summary>
    RemotePathRefused,

    /// <summary>The access password is shorter than eight characters.</summary>
    PasswordTooShort,

    /// <summary>The access password given does not match: a login, or the current password a remote change must give.</summary>
    PasswordWrong,

    /// <summary>Too many wrong passwords came from this address; logins from it wait until the pause ends.</summary>
    LoginLimited,

    /// <summary>The access password cannot be cleared while <c>server.host</c> names an extra listening address.</summary>
    PasswordClearRefused,

    /// <summary>The access password can only be cleared from this machine.</summary>
    PasswordClearRemoteRefused,
}

/// <summary>A refused facade operation. <see cref="Exception.Message"/> is localized and meant for the user.</summary>
public sealed class TacitException(TacitError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public TacitError Error { get; } = error;

    /// <summary>For a login pause: the whole seconds until logins from the address may be tried again.</summary>
    public int? RetryAfterSeconds { get; init; }
}
