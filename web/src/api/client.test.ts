import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch, onUnauthorized } from "./client";

/** Replaces the global fetch with one that answers with the given response, and returns the mock so calls can be read. */
function answerWith(response: Response) {
  const fetchMock = vi.fn<typeof fetch>(async () => response);
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("apiFetch", () => {
  it("returns the parsed body of a successful answer", async () => {
    answerWith(json({ local: true, loggedIn: false, passwordSet: false, language: "zh", version: "1.2.3" }));

    const session = await apiFetch<{ version: string }>("/api/session");

    expect(session.version).toBe("1.2.3");
  });

  it("sends the credentials with every request and the JSON content type with a body", async () => {
    const fetchMock = answerWith(json({ ok: true }));

    await apiFetch("/api/session", { method: "POST", body: JSON.stringify({ password: "x" }) });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/session");
    expect(init?.credentials).toBe("include");
    expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
  });

  it("throws an ApiError carrying the service's name and message, and does not signal unauthorized for other statuses", async () => {
    answerWith(json({ error: "PasswordTooShort", message: "访问密码至少需要 8 个字符。" }, 400));
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);

    const failure = await apiFetch("/api/access-password", { method: "PUT", body: "{}" }).catch((e: unknown) => e);

    unsubscribe();
    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 400, error: "PasswordTooShort", message: "访问密码至少需要 8 个字符。" });
    expect(listener).not.toHaveBeenCalled();
  });

  it("signals every subscriber once with the error when the session is missing, and still throws the ApiError", async () => {
    answerWith(json({ error: "ApiUnauthorized", message: "其他设备须先登录才能使用管理 API。" }, 401));
    const first = vi.fn();
    const second = vi.fn();
    const unsubscribeFirst = onUnauthorized(first);
    const unsubscribeSecond = onUnauthorized(second);

    const failure = await apiFetch("/api/status").catch((e: unknown) => e);

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

    const failure = await apiFetch("/api/access-password", { method: "PUT", body: "{}" }).catch((e: unknown) => e);

    unsubscribe();
    expect(failure).toMatchObject({ status: 401, error: "PasswordWrong" });
    expect(listener).not.toHaveBeenCalled();
  });

  it("stops signalling a subscriber once it has unsubscribed", async () => {
    answerWith(json({ error: "ApiUnauthorized", message: "x" }, 401));
    const listener = vi.fn();
    const unsubscribe = onUnauthorized(listener);
    unsubscribe();

    await apiFetch("/api/status").catch(() => undefined);

    expect(listener).not.toHaveBeenCalled();
  });

  it("gives an ApiError with a fallback message when the error body is not the service's JSON", async () => {
    answerWith(new Response("<html>bad gateway</html>", { status: 502, headers: { "Content-Type": "text/html" } }));

    const failure = await apiFetch("/api/status").catch((e: unknown) => e);

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 502, error: "unexpected" });
    expect((failure as ApiError).message).toBe("HTTP 502");
  });

  it("resolves to undefined for an answer without a body", async () => {
    answerWith(new Response(null, { status: 204 }));

    await expect(apiFetch("/api/session", { method: "DELETE" })).resolves.toBeUndefined();
  });
});
