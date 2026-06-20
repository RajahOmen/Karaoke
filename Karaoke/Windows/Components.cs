using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace Karaoke.Windows;

public static class Components
{
    public static void DrawPlaybackBar(float currentTime, float totalTime, float loopTime, float opacity)
    {
        var drawList = ImGui.GetWindowDrawList();

        var barWidth = ImGui.GetContentRegionAvail().X;

        var barHeight = ImGui.GetTextLineHeightWithSpacing();

        var progressWidth = (currentTime / totalTime) * barWidth;
        var loopWidth = (loopTime / totalTime) * barWidth;

        var style = StyleModelV1.GetFromCurrent();

        var halfScaleFactor = (0.5f + 0.5f * ImGuiHelpers.GlobalScale);
        var rounding = 5 * ImGuiHelpers.GlobalScale;
        var curPos = ImGui.GetCursorScreenPos();
        var lineOpacity = Math.Clamp(opacity, 0.3f, 1f);
        opacity = Math.Clamp(opacity, 0.4f, 0.6f);

        var grey = ImGui.GetColorU32((style.BuiltInColors?.DalamudGrey3 ?? Vector4.Zero) with { W = opacity });
        var red = ImGui.GetColorU32((style.BuiltInColors?.DalamudRed ?? Vector4.Zero) with { W = opacity * 0.7f });
        var lightGrey = (style.BuiltInColors?.DalamudGrey3 ?? Vector4.Zero) with { W = lineOpacity };
        var mult = 0.4f;
        var loopLineColor = ImGui.GetColorU32(lightGrey * new Vector4(mult, mult, mult, 1f));

        drawList.AddRectFilled(curPos, curPos + new Vector2(barWidth, barHeight), grey, rounding: rounding);
        if (loopWidth > rounding / 2f)
            drawList.AddLine(curPos + new Vector2(loopWidth, halfScaleFactor), curPos + new Vector2(loopWidth, barHeight - halfScaleFactor), loopLineColor, thickness: 2 * halfScaleFactor);

        if (progressWidth > 0)
        {
            if (progressWidth <= rounding)
            {
                drawList.PushClipRect(curPos, curPos + new Vector2(progressWidth, barHeight));
                drawList.AddRectFilled(curPos, curPos + new Vector2(rounding, barHeight), red, rounding: rounding, ImDrawFlags.RoundCornersLeft);
                drawList.PopClipRect();
            }
            else
            {
                drawList.AddRectFilled(curPos, curPos + new Vector2(progressWidth, barHeight), red, rounding: rounding, ImDrawFlags.RoundCornersLeft);
            }
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGuiHelpers.CenteredText($"{Util.FormatTime(currentTime, padMins: false)} / {Util.FormatTime(totalTime, padMins: false)}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Loop Start: {Util.FormatTime(loopTime, padMins: false)}");
        }
    }

    private static void drawTextInProgress(
        string text,
        Vector2 textSize,
        float startWidth,
        float progressStart,
        float progressWidth,
        bool debugMode = false
    )
    {

        ImGui.SetCursorPosX(startWidth);
        var cursorPos = ImGui.GetCursorPos();
        var screenPos = ImGui.GetCursorScreenPos();
        var style = StyleModelV1.GetFromCurrent();
        var red = ImGui.GetColorU32(style.BuiltInColors?.DalamudRed ?? Vector4.Zero);
        var redThin = ImGui.GetColorU32((style.BuiltInColors?.DalamudRed ?? Vector4.Zero) with { W = 0.25f });
        var drawList = ImGui.GetWindowDrawList();

        if (!debugMode)
        {
            drawList.PushClipRect(screenPos + new Vector2(progressWidth, 0), screenPos + textSize);
            ImGui.Text(text);
            drawList.PopClipRect();

            if (progressStart > 0)
            {
                ImGui.SetCursorPos(cursorPos);
                drawList.PushClipRect(screenPos, screenPos + new Vector2(progressStart, textSize.Y));
                ImGui.Text(text);
                drawList.PopClipRect();
            }
        }

        using (ImRaii.PushColor(ImGuiCol.Text, red, !debugMode))
        {
            ImGui.SetCursorPos(cursorPos);

            if (debugMode)
            {
                drawList.AddRectFilled(screenPos + new Vector2(progressStart, 0), screenPos + new Vector2(progressWidth, textSize.Y), redThin);
            }
            else
            {
                drawList.PushClipRect(screenPos + new Vector2(progressStart, 0), screenPos + new Vector2(progressWidth, textSize.Y));
            }
            ImGui.Text(text);

            if (!debugMode)
            {
                drawList.PopClipRect();
            }
            else
            {
                // text bounding box (debugging)
                drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), redThin);
            }

        }



    }

    public static void DrawCurrentText(
        string text,
        float currentTime,
        float startTime,
        float endTime,
        bool debugMode = false,
        HighlightLyricType highlightProgress = HighlightLyricType.None
    )
    {
        var textSize = ImGui.CalcTextSize(text);
        var availWidth = ImGui.GetWindowWidth();
        var textStartWidth = (availWidth - textSize.X) / 2;

        if (highlightProgress == HighlightLyricType.None)
        {
            ImGui.SetCursorPosX(textStartWidth);
            ImGui.Text(text);
            return;
        }


        var progress = Math.Clamp((currentTime - startTime) / (endTime - startTime), 0f, 1f);
        var progressWidth = (float)Math.Floor(progress * textSize.X);

        drawTextInProgress(
            text,
            textSize,
            textStartWidth,
            progressStart: 0f,
            progressWidth,
            debugMode
        );
    }


    public static void DrawCurrentLyric(
        float currentTime,
        LyricLine currentLine,
        IPluginLog log,
        bool debugMode = false,
        HighlightLyricType highlightProgress = HighlightLyricType.None
    )
    {
        var lyricText = currentLine.ToDisplayString();
        var textSize = ImGui.CalcTextSize(lyricText);
        var availWidth = ImGui.GetWindowWidth();
        var textStartWidth = (availWidth - textSize.X) / 2;
        ImGui.SetCursorPosX(textStartWidth);

        if (highlightProgress == HighlightLyricType.None)
        {
            ImGui.Text(lyricText);
            return;
        }

        var segIdx = currentLine.GetSegmentIdxAtTime(currentTime);

        var style = StyleModelV1.GetFromCurrent();
        var drawList = ImGui.GetWindowDrawList();

        var screenPos = ImGui.GetCursorScreenPos();

        var segment = currentLine.Segments[segIdx];
        var segDuration = segIdx == currentLine.Segments.Length - 1
            ? currentLine.StartTime + currentLine.DurationActive - segment.StartTime
            : currentLine.Segments[segIdx + 1].StartTime - segment.StartTime;
        var segmentProgress = Math.Clamp((currentTime - segment.StartTime) / segDuration, 0f, 1f);

        var segmentText = lyricText[segment.StartIdx..segment.EndIdx];
        var segmentSize = ImGui.CalcTextSize(segmentText.TrimEnd());

        var alreadyDrawnText = lyricText[..segment.StartIdx];
        var alreadyDrawnSize = ImGui.CalcTextSize(alreadyDrawnText);

        var progressWidth = highlightProgress switch
        {
            HighlightLyricType.ProgressSweep => alreadyDrawnSize.X + (float)Math.Ceiling(segmentSize.X * segmentProgress),
            HighlightLyricType.Word => segmentProgress > 0 ? (float)Math.Floor(alreadyDrawnSize.X + segmentSize.X) : alreadyDrawnSize.X,
            _ => 0f
        };

        if (debugMode)
        {
            var grey = ImGui.GetColorU32((style.BuiltInColors?.DalamudGrey2 ?? Vector4.Zero) with { W = 0.5f });
            drawList.AddRectFilled(screenPos + new Vector2(alreadyDrawnSize.X, 0), screenPos + new Vector2(progressWidth, textSize.Y), grey);
            drawList.AddRect(screenPos + new Vector2(alreadyDrawnSize.X, 0), screenPos + new Vector2(alreadyDrawnSize.X + segmentSize.X, textSize.Y), grey);
            //log.Verbose($"[{progressWidth}] [{textSize.X}] [{alreadyDrawnSize.X}] [{segmentSize.X}] [{segmentProgress:F2}] [{alreadyDrawnText}] [{segmentText}]");
        }

        var progressStart = highlightProgress == HighlightLyricType.Word
            ? ImGui.CalcTextSize(alreadyDrawnText.Trim()).X
            : 0f;

        drawTextInProgress(
            lyricText,
            textSize,
            textStartWidth,
            progressStart,
            progressWidth,
            debugMode
        );
    }
}
