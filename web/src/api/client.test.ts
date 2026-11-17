import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, call, onUnauthorized } from "./client";

/** Replaces the global fetch with one that answers with the given response, and returns the mock so calls can be read. */
function answerWith(response: Response) {
  const fetchMock = vi.fn<(request: Request) => Promise<Response>>(async () => response);
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

/** The request the client sent. */
function sent(fetchMock: ReturnType<typeof answerWith>): Request {
  const request = fetchMock.mock.calls[0]?.[0];
  if (request === undefined) {
    throw new Error("Nothing was sent.");
  }
  return request;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("call", () => {
  it("returns the parsed body of a successful answer", async () => {
    answerWith(json({ local: true, loggedIn: false, passwordSet: false, language: "zh", version: "1.2.3" }));

    const session = await call(api.GET("/api/session"));

    expect(session.version).toBe("1.2.3");
  });

  it("sends the credentials with every request and the JSON content type with a body", async () => {
    const fetchMock = answerWith(json({ ok: true }));

    await call(api.POST("/api/session", { body: { password: "x" } }));

    const request = sent(fetchMock);
    expect(request.url).toBe("http://tacit.test/api/session");
    expect(request.credentials).toBe("include");
    expect(request.headers.get("Content-Type")).toBe("application/json");
    expect(await request.text()).toBe('{"password":"x"}');
  });

  it("sends the JSON content type with a post that has no body, as the service requires of every post", async () => {
    const fetchMock = answerWith(json({ ok: true }));

    await call(api.POST("/api/reindex"));

    expect(sent(fetchMock).headers.get("Content-Type")).toBe("application/json");
  });

  it("puts the path and query parameters into the address, escaped", async () => {
    const fetchMock = answerWith(json({ date: "2026-11-17", entries: [] }));

    await call(api.GET("/api/logs/{date}", { params: { path: { date: "2026/11" }, query: { after: "3" } } }));

    expect(sent(fetchMock).url).toBe("http://tacit.test/api/logs/2026%2F11?after=3");
  });

  it("throws an ApiError carrying the service's name and message, and does not signal unauthorized for other statuses", async () => {
    answerWith(json({ error: "PasswordTooShort", message: "访问密码至少需要 8 个字符。" }, 400));
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);

    const failure = await call(api.PUT("/api/access-password", { body: { password: "x" } })).catch((e: unknown) => e);

    unsubscribe();
    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 400, error: "PasswordTooShort", message: "访问密码至少需要 8 个字符。" });
    expect(listener).not.toHaveBeenCalled();
  });

  it("carries every problem of a refusal that lists them", async () => {
    const problems = [{ path: "backup.keep", message: "超出范围" }];
    answerWith(json({ error: "ConfigInvalid", message: "超出范围", problems }, 409));

    const failure = await call(api.GET("/api/config")).catch((e: unknown) => e);

    expect(failure).toMatchObject({ error: "ConfigInvalid", problems });
  });

  it("signals every subscriber once with the error when the session is missing, and still throws the ApiError", async () => {
    answerWith(json({ error: "ApiUnauthorized", message: "其他设备须先登录才能使用管理 API。" }, 401));
    const first = vi.fn();
    const second = vi.fn();
    const unsubscribeFirst = onUnauthorized(first);
    const unsubscribeSecond = onUnauthorized(second);

    const failure = await call(api.GET("/api/status")).catch((e: unknown) => e);

    unsubscribeFirst();
    unsubscribeSecond();
    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 401, error: "ApiUnauthorized" });
    expect(first).toHaveBeenCalledTimes(1);
    expect(first).toHaveBeenCalledWith(failure);
    expect(second).toHaveBeenCalledTimes(1);
  });

  it("does not signal for a 401 that is a wrong password, which is an answer to the request and not a lost session", async () => {
    answerWith(json({ error: "PasswordWrong", message: "访问密码不对。" }, 401));
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);

    const failure = await call(api.PUT("/api/access-password", { body: { password: "x" } })).catch((e: unknown) => e);

    unsubscribe();
    expect(failure).toMatchObject({ status: 401, error: "PasswordWrong" });
    expect(listener).not.toHaveBeenCalled();
  });

  it("stops signalling a subscriber once it has unsubscribed", async () => {
    answerWith(json({ error: "ApiUnauthorized", message: "x" }, 401));
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);
    unsubscribe();

    await call(api.GET("/api/status")).catch(() => undefined);

    expect(listener).not.toHaveBeenCalled();
  });

  it("gives an ApiError without a name and with an empty message when the error body is not the service's JSON", async () => {
    answerWith(new Response("<html>bad gateway</html>", { status: 502, headers: { "Content-Type": "text/html" } }));

    const failure = await call(api.GET("/api/status")).catch((e: unknown) => e);

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 502, error: null, message: "" });
  });

  it("resolves to undefined for an answer without a body", async () => {
    answerWith(new Response(null, { status: 204 }));

    await expect(call(api.DELETE("/api/session"))).resolves.toBeUndefined();
  });
});
