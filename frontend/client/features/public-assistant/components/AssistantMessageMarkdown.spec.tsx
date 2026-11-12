import React from "react";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import AssistantMessageMarkdown from "./AssistantMessageMarkdown";

describe("AssistantMessageMarkdown", () => {
  it("formats headings, emphasis, lists, and safe links without parsing HTML", () => {
    const { container } = render(
      <AssistantMessageMarkdown
        text={[
          "## 办理安排",
          "",
          "**接待时间**：09:17 开始。",
          "",
          "1. 从北门进入。",
          "2. [查看服务说明](https://example.test/service)",
          "",
          "联系网址：https://example.test/contact?mode=h5，",
          "<img src=x onerror=alert(1)>",
          "[危险链接](javascript:alert(1))",
        ].join("\n")}
      />,
    );

    expect(
      screen.getByRole("heading", { level: 2, name: "办理安排" }),
    ).toBeInTheDocument();
    expect(screen.getByText("接待时间")).toHaveProperty("tagName", "STRONG");
    expect(screen.getByRole("list").tagName).toBe("OL");
    expect(
      screen.getByRole("link", { name: "查看服务说明" }),
    ).toHaveAttribute("href", "https://example.test/service");
    expect(
      screen.getByRole("link", { name: "https://example.test/contact?mode=h5" }),
    ).toHaveAttribute("href", "https://example.test/contact?mode=h5");
    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector('a[href^="javascript:"]')).toBeNull();
    expect(container).toHaveTextContent("<img src=x onerror=alert(1)>");
    expect(container).toHaveTextContent("[危险链接](javascript:alert(1))");
  });
});
