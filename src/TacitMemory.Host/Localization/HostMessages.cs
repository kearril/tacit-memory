using System.Net;
using System.Net.Sockets;
using TacitMemory.Core;
using TacitMemory.Core.Localization;

namespace TacitMemory.Host.Localization;

/// <summary>
/// Human-facing text of the host, in the memory language: the lines <c>tacit serve</c> prints, the refusals of the
/// management API's guards and connection rule, and the answers to a request or a failure no operation words itself.
/// </summary>
public static class HostMessages
{
    public static readonly Localized ServiceListening = new(
        "常驻服务已启动：{0}（Ctrl+C 停止）",
        "The memory service is running at {0} (Ctrl+C to stop)");
    public static readonly Localized PortInUse = new(
        "端口 {0} 已被占用，常驻服务无法启动。请在 {1} 中修改 server.port。",
        "Port {0} is already in use, so the memory service cannot start. Change server.port in {1}.");

    public static readonly Localized ApiOriginRefused = new(
        "管理 API 只接受与请求自身同源的 Origin。",
        "The management API only accepts an Origin that is the origin of the request itself.");

    public static readonly Localized ApiHostRefused = new(
        "管理 API 对本机连接只接受 Host 为 127.0.0.1、localhost 或 [::1] 的请求。",
        "The management API only accepts local requests whose Host is 127.0.0.1, localhost or [::1].");

    public static readonly Localized ApiContentTypeRefused = new(
        "管理 API 的 POST 与 PUT 请求必须是 Content-Type: application/json。",
        "POST and PUT requests to the management API must have Content-Type: application/json.");

    public static readonly Localized ApiUnauthorized = new(
        "其他设备须先登录才能使用管理 API。",
        "Other devices must log in before they can use the management API.");

    public static readonly Localized WebNotBuilt = new(
        "Web 界面未构建",
        "The web interface is not built");

    public static readonly Localized McpRemoteRefused = new(
        "MCP 端点只接受本机连接。",
        "The MCP endpoint only accepts connections from this machine.");

    public static readonly Localized RemotePathRefused = new(
        "其他设备只能访问 /、/ui 与 /api 下的路径，其余路径一律拒绝。",
        "Other devices may only use /, /ui and /api; every other path is refused.");

    public static readonly Localized ExtraPortInUse = new("端口 {0} 已被占用", "port {0} is already in use");

    public static readonly Localized ExtraAddressUnavailable = new("{0} 不是本机的地址", "{0} is not an address of this machine");

    public static readonly Localized ApiBodyInvalid = new(
        "请求体无效：应为 JSON 对象，格式见管理 API 说明。",
        "The request body is invalid: it must be a JSON object, as the management API describes.");

    public static readonly Localized ApiBodyInvalidField = new(
        "请求体无效：字段 {0} 的类型或取值不对。",
        "The request body is invalid: the value of {0} has the wrong type or value.");

    public static readonly Localized ApiFileFailed = new(
        "无法读写文件或文件夹：{0}",
        "A file or folder cannot be read or written: {0}");

    public static readonly Localized ApiInternalFailure = new(
        "服务内部出错，详情见 logs 目录中的日志。",
        "The service failed unexpectedly; the details are in the logs folder.");

    /// <summary>The message for a request the host refuses: the management API's guards, its connection rule, or the MCP endpoint's.</summary>
    public static string Refusal(TacitError error, MemoryLanguage language) => error switch
    {
        TacitError.ApiOriginRefused => ApiOriginRefused.In(language),
        TacitError.ApiHostRefused => ApiHostRefused.In(language),
        TacitError.ApiContentTypeRefused => ApiContentTypeRefused.In(language),
        TacitError.ApiUnauthorized => ApiUnauthorized.In(language),
        TacitError.McpRemoteRefused => McpRemoteRefused.In(language),
        TacitError.RemotePathRefused => RemotePathRefused.In(language),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a host refusal."),
    };

    /// <summary>Why the extra listener at <paramref name="address"/> and <paramref name="port"/> did not bind: the system's error, put in words where it can be.</summary>
    public static string ExtraListenerFailure(Exception failure, IPAddress address, int port, MemoryLanguage language) => failure is SocketException socket
        ? socket.SocketErrorCode switch
        {
            SocketError.AddressAlreadyInUse => ExtraPortInUse.Format(language, port),
            SocketError.AddressNotAvailable => ExtraAddressUnavailable.Format(language, address),
            _ => socket.Message,
        }
        : failure.Message;
}
