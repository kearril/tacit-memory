/**
 * The instruction the index card copies for the user to paste into a local agent, so that the agent sets up a daily task
 * in its own environment that asks the service whether a consolidation is due. It is for an agent, so it is English
 * whatever the interface language; only the address of the service changes.
 */
export function scheduleInstruction(consolidationUrl: string): string {
  return [
    "Set up a scheduled task in your environment that checks whether my TacitMemory memory needs consolidating:",
    "",
    "1. The task must run on this computer: the TacitMemory service accepts only local connections, so a task that runs in the cloud cannot reach it. If you have no way to schedule a task that runs on this computer, tell me so and do not set one up another way.",
    "2. Ask me what time of day it should run, and run it once a day at that time. Do not pick a time yourself.",
    `3. Each run: send \`GET ${consolidationUrl}\`. If the JSON response has \`"due": true\`, remind me to run the tacit-consolidate skill. If it has \`"due": false\`, do nothing. Never start a consolidation yourself.`,
    "4. When it is set up, tell me the task's name, when it runs, and how to see and remove it.",
  ].join("\n");
}
