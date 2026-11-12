// Spec: specs/136-public-assistant/results/input-character-limits-20261110.md
import React, { useState } from "react";
import "@testing-library/jest-dom/vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  AssistantCountedInput,
  AssistantCountedTextarea,
} from "./AssistantCountedField";

afterEach(cleanup);

describe.each([AssistantCountedInput, AssistantCountedTextarea])(
  "counted text control",
  (Control) => {
    function Field({
      initial = "",
      onCommit = vi.fn(),
    }: {
      initial?: string;
      onCommit?: (value: string) => void;
    }) {
      const [value, setValue] = useState(initial);
      return (
        <Control
          aria-label="内容"
          data-testid="field"
          value={value}
          maxLength={5}
          onChange={(event) => {
            setValue(event.target.value);
            onCommit(event.target.value);
          }}
        />
      );
    }

    it("keeps IME candidates intact until confirmation then limits the committed text", () => {
      const onCommit = vi.fn();
      render(<Field initial="已有内容" onCommit={onCommit} />);
      const input = screen.getByRole("textbox");
      fireEvent.compositionStart(input);
      fireEvent.change(input, { target: { value: "已有内容中文" } });
      expect(input).toHaveValue("已有内容中文");
      expect(onCommit).not.toHaveBeenCalled();
      fireEvent.compositionEnd(input, { data: "中文" });
      expect(input).toHaveValue("已有内容中");
      expect(onCommit).toHaveBeenLastCalledWith("已有内容中");
      expect(input).toHaveAttribute("maxlength", "5");
      expect(screen.getByTestId("field-count")).toHaveTextContent("5 / 5");
    });

    it("retains the suffix when pasting over a selection at the limit", () => {
      render(<Field initial="abcde" />);
      const input = screen.getByRole("textbox") as HTMLInputElement;
      input.setSelectionRange(1, 3);
      fireEvent.paste(input, { clipboardData: { getData: () => "中文多余" } });
      expect(input).toHaveValue("a中文de");
      fireEvent.change(input, { target: { value: "a中文d" } });
      expect(input).toHaveValue("a中文d");
      expect(screen.getByTestId("field-count")).toHaveTextContent("4 / 5");
    });

    it("does not split an emoji when pasting or completing composition", () => {
      render(<Field initial="四个汉字" />);
      const input = screen.getByRole("textbox") as HTMLInputElement;
      input.setSelectionRange(4, 4);
      fireEvent.paste(input, { clipboardData: { getData: () => "😀后续" } });
      expect(input).toHaveValue("四个汉字");
      fireEvent.compositionStart(input);
      fireEvent.change(input, { target: { value: "四个汉字😀" } });
      fireEvent.compositionEnd(input);
      expect(input).toHaveValue("四个汉字");
    });

    it("keeps an invalid loaded value intact until the user edits it", () => {
      render(<Field initial="旧数据已经超长" />);
      expect(screen.getByRole("textbox")).toHaveValue("旧数据已经超长");
      expect(screen.getByTestId("field-count")).toHaveTextContent("7 / 5");
      fireEvent.change(screen.getByRole("textbox"), {
        target: { value: "已修正" },
      });
      expect(screen.getByRole("textbox")).toHaveValue("已修正");
    });
  },
);
