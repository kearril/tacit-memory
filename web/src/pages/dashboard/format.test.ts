import { describe, expect, it } from "vitest";
import { clockOf, dayText, durationOf, fileNameOf, remainingOf, sizeOf, versionText } from "./format";

describe("durationOf", () => {
  it("splits seconds into whole days, hours and minutes", () => {
    expect(durationOf(3 * 86400 + 4 * 3600 + 12 * 60 + 59)).toEqual({ days: 3, hours: 4, minutes: 12 });
  });

  it("is all zero below a minute, and for a negative count", () => {
    expect(durationOf(59)).toEqual({ days: 0, hours: 0, minutes: 0 });
    expect(durationOf(-5)).toEqual({ days: 0, hours: 0, minutes: 0 });
  });
});

describe("remainingOf", () => {
  it("counts down by the whole seconds since the report was fetched, and stops at zero", () => {
    expect(remainingOf(120, 10_000, 10_999)).toBe(120);
    expect(remainingOf(120, 10_000, 13_500)).toBe(117);
    expect(remainingOf(2, 10_000, 15_000)).toBe(0);
  });
});

describe("clockOf", () => {
  it("shows minutes and two-digit seconds, and hours from an hour up", () => {
    expect(clockOf(5)).toBe("0:05");
    expect(clockOf(272)).toBe("4:32");
    expect(clockOf(3723)).toBe("1:02:03");
  });
});

describe("sizeOf", () => {
  it("takes the largest unit that keeps the number at one or more, with at most one decimal", () => {
    expect(sizeOf(512, "en")).toBe("512 byte");
    expect(sizeOf(1536, "en")).toBe("1.5 kB");
    expect(sizeOf(3.44 * 1024 * 1024, "en")).toBe("3.4 MB");
  });
});

describe("dayText", () => {
  it("shows the calendar day itself, whatever the browser's time zone", () => {
    expect(dayText("2026-11-01", "en", { month: "short", day: "numeric" })).toBe("Nov 1");
  });
});

describe("fileNameOf", () => {
  it("takes the last part of a Windows or a POSIX path", () => {
    expect(fileNameOf("C:\\tacit\\backups\\manual-2026-11-13-200859.zip")).toBe("manual-2026-11-13-200859.zip");
    expect(fileNameOf("/home/me/.tacit/backups/auto-2026-11-13.zip")).toBe("auto-2026-11-13.zip");
  });
});

describe("versionText", () => {
  it("drops the build metadata, and leaves a plain version alone", () => {
    expect(versionText("1.0.0+cd36d27")).toBe("1.0.0");
    expect(versionText("1.0.0")).toBe("1.0.0");
  });
});
