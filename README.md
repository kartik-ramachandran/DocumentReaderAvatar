# Avatar Doc Reader

A POC that combines an **Azure Talking Avatar** with **Azure OpenAI gpt-4o** (including vision). Upload documents, images, or PDFs and talk to a live avatar that reads and answers questions about your content using your voice.

---

## What You Need Before Running This

Two Azure resources are required. Everything else is optional.

### 1. Azure Speech Service
- **Kind:** Speech Services (S0)
- **Region:** Must be one that supports Azure Talking Avatar — e.g. `westus2`, `eastus`, `northeurope`
- **What you get:** Subscription Key + Region
- Fill in `AzureSpeech:SubscriptionKey` and `AzureSpeech:Region` in `appsettings.json`

### 2. Azure OpenAI
- **Kind:** Azure OpenAI
- **Model deployment:** Deploy `gpt-4o` (vision-capable)
- **What you get:** Endpoint URL, API Key, Deployment name
- Fill in `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, and `AzureOpenAI:Deployment` in `appsettings.json`

### Optional: Direct OpenAI API
- Only needed if you want PDFs and images sent to OpenAI's file/vision API instead of Azure OpenAI
- Fill in `OpenAI:ApiKey` in `appsettings.json`

> **Never commit `appsettings.json` with real keys.** Use environment variables or Azure Key Vault for production.
> For container deployments, secrets are passed as environment variables — not baked into the image.

---

## Architecture

```
Browser (Vue 3)
  │
  ├── Mic input ──► Azure Speech SDK (continuous STT) ──► /api/chat
  │                                                           │
  │                                               Azure OpenAI gpt-4o
  │                                               (text + vision for images/PDFs)
  │                                                           │
  └── Avatar video ◄── Azure Talking Avatar ◄────────────────┘
        (WebRTC)           (Speech Service)
```

### Flow

1. User speaks → Azure Speech SDK transcribes continuously in the background
2. When the user stops talking, the transcript is sent automatically to `/api/chat`
3. Backend searches the uploaded knowledge library for relevant content
4. Matched text + images sent to **Azure OpenAI gpt-4o** (vision-capable)
5. Model returns a plain spoken answer (no markdown, no bullets)
6. **Azure Talking Avatar** speaks the answer via WebRTC video stream

---

## Azure Resources Required

| Resource | Kind | Notes |
|---|---|---|
| Azure Speech Service | Speech Services S0 | Must be in an Avatar-supported region (e.g. `westus2`, `eastus`, `northeurope`) |
| Azure OpenAI | Azure OpenAI | Deploy `gpt-4o`; any supported region works |

**Not required:** Azure Communication Services (ACS) — the Speech Service has its own built-in WebRTC relay endpoint (`tts.speech.microsoft.com/cognitiveservices/avatar/relay/token/v1`).

### Why a specific region for Speech?

Azure Talking Avatar real-time streaming is only available in specific regions. Check the [Azure docs](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/text-to-speech-avatar/what-is-text-to-speech-avatar) for the current supported region list.

---

## Container Deployment

### Image

Single Docker image — nginx serves the Vue frontend on port 80 and proxies `/api/` internally to the .NET API on port 5000.

```
<your-acr>.azurecr.io/avatardocreader:<yyyyMMdd-HHmmss>
```

### Deploy a new version

```powershell
# 1. Build and tag with timestamp
$tag = Get-Date -Format "yyyyMMdd-HHmmss"
docker build -t <your-acr>.azurecr.io/avatardocreader:$tag .

# 2. Push
az acr login --name <your-acr>
docker push <your-acr>.azurecr.io/avatardocreader:$tag

# 3. Update container app
az containerapp update `
  --name <your-container-app-name> `
  --resource-group <your-resource-group> `
  --image <your-acr>.azurecr.io/avatardocreader:$tag
```

### Container App setup

- Create a Container App in any environment/region
- Assign a system-assigned managed identity with `AcrPull` role on your ACR (avoids storing registry credentials)
- Pass secrets as environment variables on the Container App (see Configuration below)

---

## Running Locally

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Node.js 20+

### Backend

```bash
cd api
dotnet run
# http://localhost:5158
```

### Frontend

```bash
cd web
npm install
npm run dev -- --port 5174
# http://localhost:5174
```

---

## Configuration

`api/appsettings.json`:

```json
{
  "AzureSpeech": {
    "SubscriptionKey": "<your-azure-speech-subscription-key>",
    "Region": "<your-azure-speech-region>",
    "AvatarCharacter": "lisa",
    "AvatarStyle": "casual-sitting"
  },
  "AzureOpenAI": {
    "Endpoint": "https://<your-azure-openai-resource>.openai.azure.com/",
    "ApiKey": "<your-azure-openai-api-key>",
    "Deployment": "<your-gpt-4o-deployment-name>",
    "ApiVersion": "2024-10-21"
  },
  "OpenAI": {
    "ApiKey": "",
    "Model": "gpt-4.1"
  }
}
```

> `OpenAI` (direct) is optional. When configured it enables sending PDFs and images to OpenAI's file/vision API as an alternative to Azure OpenAI vision.

---

## Features

### Talking Avatar
- Azure Talking Avatar over real-time WebRTC
- Characters: **Lisa, Anna, Harry, Jeff, Max** — each with multiple styles
- Select character and style before starting

### Voice Interaction
- Mic starts automatically when the avatar starts
- Continuous speech recognition — no push-to-talk needed
- Sends transcript automatically when you stop talking (like ChatGPT voice mode)
- Round mic button: **green pulsing** = listening, **red** = muted
- Click to toggle mic on/off

### Document Library

| Format | Processing |
|---|---|
| PDF | Text extracted (PdfPig) + sent to gpt-4o vision |
| Word (.docx) | Full text extracted via OpenXml |
| Excel (.xlsx) | All cell values extracted as text |
| PowerPoint (.pptx) | All slide text extracted |
| Image (jpg, png, etc.) | Sent directly to gpt-4o vision |
| Text, Markdown, JSON, XML, CSV, YAML, code files | Read directly |
| Audio / Video | Stored as metadata (transcription not yet implemented) |

- Upload individual files or entire folders (relative paths preserved)
- All extracted text is indexed and searched for relevant context
- Images and PDFs are **always included** in context regardless of keyword score

### Answers
- Plain spoken language — no markdown, bullets, or hashtags
- Sources shown under each answer in the chat panel
- "Send PDFs/images to model" toggle — enables vision when on

---

## Answer Priority

| Condition | Behaviour |
|---|---|
| OpenAI direct configured + files present + toggle on | Files sent to OpenAI vision/file API |
| Azure OpenAI configured (default) | gpt-4o chat completions with text + inline images |
| Neither configured | Keyword-matched text snippets from library only |

---

## Project Structure

```
AvatarDocReader/
├── Dockerfile               # Combined build: Vue (nginx) + .NET API
├── start.sh                 # Entrypoint: starts dotnet + nginx
├── README.md
│
├── api/                     # .NET 10 ASP.NET Core Web API
│   ├── Endpoints/
│   │   ├── AvatarEndpoints.cs   # /api/avatar/token, /api/avatar/relay-token
│   │   ├── ChatEndpoints.cs     # /api/chat
│   │   ├── LibraryEndpoints.cs  # /api/library (upload, list, clear)
│   │   └── HealthEndpoints.cs
│   ├── Models/
│   │   ├── KnowledgeItem.cs     # File parsing: PDF, Word, Excel, PPT, image, text
│   │   └── ChatDtos.cs
│   ├── Options/
│   │   ├── AzureSpeechOptions.cs
│   │   ├── AzureOpenAiOptions.cs
│   │   ├── OpenAiOptions.cs
│   │   └── AvatarOptions.cs     # Avatar character/style catalog
│   ├── Services/
│   │   ├── KnowledgeStore.cs    # In-memory library with keyword search
│   │   └── AnswerService.cs     # LLM orchestration + vision
│   └── appsettings.json
│
└── web/                     # Vue 3 + Vite + TypeScript
    ├── nginx.conf           # Serves frontend + proxies /api/ to .NET
    └── src/
        ├── App.vue          # Avatar, mic, chat, library UI
        ├── main.ts
        └── style.css
```

---

## Avatar Characters & Styles

| Character | Styles |
|---|---|
| Lisa | casual-sitting, graceful-sitting, graceful-standing, technical-sitting, technical-standing |
| Anna | casual-sitting |
| Harry | business, casual, youthful |
| Jeff | business, formal |
| Max | business |
