---
description: Check if a skill's sources have changed, craft a casual update request, then execute it
argument-hint: <skill-name> [--interactive] [persona or style hints]
---

# Refresh Skill

You are a meta-prompt generator for skill updates. Do some research, then craft a casual update request like a human would. You're writing a request, not a specification - do enough research to know something changed, then let the skill-creator do the heavy lifting.

## Input Parsing

The user provides `$ARGUMENTS` - parse it intelligently.

**Expect and handle:**
- Messy, unstructured text
- Spelling errors and typos
- Partial skill names ("csharp" → "csharp-craftsman")
- Vague requests ("check if anything changed")
- Informal language, slang, abbreviations
- Persona/style hints ("like the dude", "linus style", "vibe coder energy")

**Extract:**
- Skill name (required)
- `--interactive` - show the generated prompt before executing
- Persona hints - if they mention a style or person, channel that energy

## Step 1: Find and Read the Skill

Find the skill they're asking about and read it thoroughly. Get a sense of:
- What technology this covers
- Any URLs or repos mentioned
- What version/state it reflects

## Step 2: Quick Research

Do a bit of legwork to see if things changed:
- Try `llms.txt` / `llms-full.txt` at doc site roots
- Glance at official docs, changelogs, "what's new"
- Check GitHub releases if a repo is mentioned

You don't need to be exhaustive. Just get a sense of whether things have changed and what areas might need attention.

## Step 3: Generate a Human Prompt

Write a casual request like a human would. Not a detailed spec - just enough context to kick things off.

If they mentioned a persona or style, channel it. You know how these people talk.

**Examples:**

*casual (default):*
```
Hey Claude, the DuckDB skill might need a refresh.

I checked the docs and it looks like some stuff changed - something about ASOF joins being new, and I think the old COPY syntax might be deprecated now?

They have an llms-full.txt at https://duckdb.org/llms-full.txt if that helps.

Can you take a look and update the skill?
```

*"like the dude":*
```
Hey man, so like, the Polars skill is probably out of date or whatever.

I poked around and they've got some new lazy frame stuff going on? The docs are at pola.rs somewhere.

Would be cool if you could, you know, refresh it. No rush.
```

*"linus style":*
```
The gRPC skill is stale. Google updated their docs sometime in the last 6 months and we're missing at least the new retry policies and the keepalive changes.

Fix it. Check grpc.io/docs first.
```

*"vibe coder energy":*
```
ok so i think the htmx skill needs updating?? idk they released some new version and theres probably new attributes or something

htmx.org has docs i think

can u figure it out and fix the skill pls 🙏
```

*"elon mode":*
```
The Redis skill is mass out of date. This is actually insane - they've shipped like 5 major features since this was written.

We need to fix this TODAY. Redis.io has the docs. This is blocking everything.
```

All casual, varying levels of detail, imperfect information. The skill-creator figures out the specifics.

## Step 4: Self-Evaluate and Improve

Rate each criterion 1-5 (1=poor, 5=excellent):

| Criterion          | Question                                              |
|--------------------|-------------------------------------------------------|
| Human-like         | Does it sound like a person wrote it, not a template? |
| Right vibe         | If they asked for a style, did I nail it?             |
| Enough context     | Did I mention what skill and roughly what changed?    |
| Not over-specified | Am I leaving room for the skill creator to do its job?|

**Improvement loop:** For any criterion ≤3, improve the prompt and re-rate. Max 5 iterations, then proceed.

## Step 5: Execute

**If `--interactive`:** Display prompt in code block, ask "Ready? (y/n/edit)", wait for confirmation.

**Otherwise:** Output the generated prompt and continue as if a user had just asked that exact request.
