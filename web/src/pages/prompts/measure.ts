import type { PromptMeasure } from "../../api/prompts";
import type { Dictionary } from "../../i18n/messages";

/** One line of a document's measure, and whether it is past its limit. */
export interface MeasureLine {
  readonly text: string;
  readonly over: boolean;
}

/**
 * The lines that show a document's measure under its text. A document with a limit of its own shows its characters; the
 * core rules and the criteria show the fixed part of the server instructions and the room it leaves for recent activity,
 * which is told as it is when there is none; the criteria add the items of each section.
 */
export function measureLines(measure: PromptMeasure, words: Dictionary["prompts"]["measure"]): readonly MeasureLine[] {
  const over = measure.length > measure.limit;
  if (measure.activityRoom === null) {
    return [{ text: words.characters(measure.length, measure.limit), over }];
  }

  const room = measure.activityRoom;
  const lines: MeasureLine[] = [
    { text: words.fixedPart(measure.length, measure.limit), over },
    room >= 0 ? { text: words.activityRoom(room), over: false } : { text: words.noActivityRoom(-room), over: true },
  ];
  const items = measure.items;
  if (items !== null) {
    lines.push(
      { text: words.items(words.worthRecording, items.worthRecording, items.limit), over: items.worthRecording > items.limit },
      { text: words.items(words.notRecorded, items.notRecorded, items.limit), over: items.notRecorded > items.limit },
    );
  }
  return lines;
}
