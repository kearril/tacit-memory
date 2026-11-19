namespace TacitMemory.Core.Localization;

/// <summary>Human-facing text of the access password: its refusals, the start refusal and the log notice for a damaged state file.</summary>
internal static class AccessMessages
{
    public static readonly Localized PasswordTooShort = new(
        "访问密码至少需要 8 个字符。",
        "The access password must be at least 8 characters.");
    public static readonly Localized PasswordWrong = new(
        "访问密码错误。",
        "The access password is wrong.");
    public static readonly Localized LoginLimited = new(
        "错误次数过多，请 {0} 秒后再试。",
        "Too many wrong passwords. Try again in {0} seconds.");
    public static readonly Localized PasswordClearRefused = new(
        "server.host 设为 {0}（额外监听地址）时不能清除访问密码，否则其他设备会在没有密码的情况下连上服务。",
        "The access password cannot be cleared while server.host is {0} (an extra listener), or other devices could reach the service without a password.");
    public static readonly Localized PasswordClearRemoteRefused = new(
        "只能在本机清除访问密码。",
        "The access password can only be cleared from this machine.");
    public static readonly Localized PasswordRequiredForExtraAddress = new(
        "server.host 设为 {0}（额外监听地址，不是 127.0.0.1）时必须先有访问密码，服务没有启动。请先删去 server.host 启动服务，在本机设置访问密码，然后再把 server.host 写回。",
        "server.host is {0}, an extra listener beyond 127.0.0.1, and it needs an access password first, so the service did not start. Remove server.host, start the service, set the access password from this machine, then write server.host back.");
    public static readonly Localized StateDamaged = new(
        "访问状态文件 {0} 无法读取，按未设访问密码处理。",
        "The access state file {0} cannot be read; it is treated as having no access password.");
}
