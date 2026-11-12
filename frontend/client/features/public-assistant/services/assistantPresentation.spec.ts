import { describe, expect, it } from "vitest";
import {
  composeAssistantWelcomeMessage,
  isSafeAssistantIconUrl,
  splitAssistantWelcomeMessage,
} from "./assistantPresentation";

describe("isSafeAssistantIconUrl", () => {
  it("allows same-site asset paths and secure image hosts only", () => {
    expect(isSafeAssistantIconUrl("/api/media/assets/asset-1/content")).toBe(true);
    expect(isSafeAssistantIconUrl("https://cdn.example.test/icon.webp")).toBe(true);
    expect(isSafeAssistantIconUrl("http://cdn.example.test/icon.webp")).toBe(false);
    expect(isSafeAssistantIconUrl("javascript:alert(1)")).toBe(false);
    expect(isSafeAssistantIconUrl("//cdn.example.test/icon.webp")).toBe(false);
  });
});

describe("splitAssistantWelcomeMessage", () => {
  it("uses the first non-empty line as the title and preserves following copy", () => {
    expect(
      splitAssistantWelcomeMessage("\n你好，今天想了解什么？\r\n报名、日程和参会问题都可以问我。"),
    ).toEqual({
      title: "你好，今天想了解什么？",
      description: "报名、日程和参会问题都可以问我。",
    });
  });

  it("keeps legacy single-line welcome messages as titles", () => {
    expect(splitAssistantWelcomeMessage("欢迎咨询默认客服")).toEqual({
      title: "欢迎咨询默认客服",
      description: "",
    });
  });
});

describe("composeAssistantWelcomeMessage", () => {
  it("stores title and supporting copy in the existing multi-line field", () => {
    expect(
      composeAssistantWelcomeMessage(
        "  你好，今天想了解什么？  ",
        "  报名、日程和参会问题都可以问我。  ",
      ),
    ).toBe("你好，今天想了解什么？\n报名、日程和参会问题都可以问我。");
  });

  it("omits the separator when the optional supporting copy is empty", () => {
    expect(composeAssistantWelcomeMessage(" 欢迎咨询 ", "  ")).toBe("欢迎咨询");
  });
});
