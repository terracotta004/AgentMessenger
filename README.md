# AgentMessenger

AgentMessenger is a .NET console application that lets AI agents message humans and other agents over:
- **Email**
- **MauiMessenger**

## What this starter app does

- Routes outgoing messages through channel-specific transports.
- Supports identities for people (email-style identifiers) and agents (`agent:*`, `bot:*`).
- Reads configuration from `appsettings.json` (or `AGENT_MESSENGER_CONFIG` env var).
- Sends and reads inbox messages from MauiMessenger over HTTP.


## Runtime target

- This starter currently targets **.NET 10** (`net10.0`).

## Project structure

- `AgentMessenger.Console/Program.cs` – CLI entry point.
- `AgentMessenger.Console/Messaging` – routing + helpers.
- `AgentMessenger.Console/Transports` – Email and MauiMessenger channel implementations.
- `AgentMessenger.Console/Configuration` – app configuration objects.

## Commands

```bash
# send a message through email
AgentMessenger.Console send \
  --from agent:planner \
  --to user@example.com \
  --channel Email \
  --subject "Plan Ready" \
  --body "I generated the itinerary."

# send a message through maui messenger
AgentMessenger.Console send \
  --from bot:support \
  --to agent:ops \
  --channel MauiMessenger \
  --subject "Escalation" \
  --body "Need approval for deploy window."

# read inbox
AgentMessenger.Console inbox --owner agent:ops --channel MauiMessenger
```

## MauiMessenger changes needed

To connect MauiMessenger with AgentMessenger, MauiMessenger should expose:

1. `POST /api/messages`
   - Accept payload fields:
     - `id`
     - `from`
     - `to`
     - `subject`
     - `body`
     - `sentAtUtc`
     - `metadata`
   - Validate `X-AgentMessenger-Key` header.

2. `GET /api/messages/inbox/{identity}`
   - Returns an array of message objects using the same schema.

You can adjust these paths in `appsettings.json` if MauiMessenger already has different routes.

## Notes

- The Email transport currently stores messages in-memory as a local dev stub.
- Swap the Email transport implementation to a real SMTP provider when ready.
