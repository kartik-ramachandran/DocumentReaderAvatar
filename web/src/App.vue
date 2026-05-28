<script setup lang="ts">
import { computed, ref } from 'vue'

type KnowledgeItem = {
  id: string
  name: string
  path: string
  kind: 'text' | 'image' | 'pdf' | 'audio' | 'video' | 'other'
  size: number
  description: string
}

type ChatMessage = {
  role: 'user' | 'assistant'
  text: string
  sources?: KnowledgeItem[]
}

type AvatarOption = {
  character: string
  label: string
  gender: string
  styles: string[]
}

const apiBase = import.meta.env.VITE_API_BASE ?? 'http://localhost:5158'

const avatarOptions: AvatarOption[] = [
  { character: 'lisa', label: 'Lisa', gender: 'Female', styles: ['casual-sitting', 'graceful-sitting', 'graceful-standing', 'technical-sitting', 'technical-standing'] },
  { character: 'anna', label: 'Anna', gender: 'Female', styles: ['casual-sitting'] },
  { character: 'harry', label: 'Harry', gender: 'Male', styles: ['business', 'casual', 'youthful'] },
  { character: 'jeff', label: 'Jeff', gender: 'Male', styles: ['business', 'formal'] },
  { character: 'max', label: 'Max', gender: 'Male', styles: ['business'] },
]

const library = ref<KnowledgeItem[]>([])
const messages = ref<ChatMessage[]>([
  {
    role: 'assistant',
    text: 'Upload documents, folders, images, audio, or videos. I can read text now and keep media ready for OCR, transcription, or visual analysis extensions.',
  },
])
const prompt = ref('')
const uploadBusy = ref(false)
const chatBusy = ref(false)
const avatarBusy = ref(false)
const avatarActive = ref(false)
const listening = ref(false)
const sendFilesToModel = ref(true)
const selectedAvatarCharacter = ref('lisa')
const selectedAvatarStyle = ref('casual-sitting')
const avatarVideo = ref<HTMLVideoElement | null>(null)

let avatarSynthesizer: any = null
let peerConnection: RTCPeerConnection | null = null
let speechSdk: any = null
let speechRecognizer: any = null

const selectedAvatar = computed(() =>
  avatarOptions.find(a => a.character === selectedAvatarCharacter.value) ?? avatarOptions[0]!,
)

const selectedAvatarStyles = computed(() => selectedAvatar.value.styles)

const stats = computed(() => {
  const counts = { text: 0, image: 0, pdf: 0, audio: 0, video: 0, other: 0 }
  for (const item of library.value) counts[item.kind] += 1
  return counts
})

function formatStyle(style: string) {
  return style.split('-').map(part => part.charAt(0).toUpperCase() + part.slice(1)).join(' ')
}

function onAvatarChange() {
  const avatar = selectedAvatar.value
  if (!avatar.styles.includes(selectedAvatarStyle.value)) {
    selectedAvatarStyle.value = avatar.styles[0] ?? ''
  }
}

async function uploadFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  if (files.length === 0) return

  uploadBusy.value = true
  messages.value.push({ role: 'assistant', text: `Processing ${files.length} file${files.length === 1 ? '' : 's'}... extracting content, please wait.` })
  if (avatarActive.value) speak(`Processing ${files.length} file${files.length === 1 ? '' : 's'}. Please wait while I extract the content.`)
  try {
    const form = new FormData()
    for (const file of files) {
      form.append('files', file)
      form.append('paths', (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name)
    }

    const response = await fetch(`${apiBase}/api/library/files`, { method: 'POST', body: form })
    if (!response.ok) throw new Error(await response.text())
    await refreshLibrary()
    const doneText = `Done. ${files.length} file${files.length === 1 ? '' : 's'} processed and ready. You can now ask me questions about the content.`
    messages.value.push({ role: 'assistant', text: doneText })
    if (avatarActive.value) speak(doneText)
  } catch (error) {
    messages.value.push({ role: 'assistant', text: `Upload failed: ${error instanceof Error ? error.message : 'Unknown error'}` })
  } finally {
    uploadBusy.value = false
    input.value = ''
  }
}

async function refreshLibrary() {
  const response = await fetch(`${apiBase}/api/library`)
  if (!response.ok) return
  const data = await response.json()
  library.value = data.items ?? []
}

async function clearLibrary() {
  await fetch(`${apiBase}/api/library`, { method: 'DELETE' })
  library.value = []
  messages.value = [{ role: 'assistant', text: 'Library cleared. Upload a new set of files whenever you are ready.' }]
}

async function ask() {
  const text = prompt.value.trim()
  if (!text || chatBusy.value) return

  prompt.value = ''
  messages.value.push({ role: 'user', text })
  chatBusy.value = true
  try {
    const response = await fetch(`${apiBase}/api/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text, sendFilesToModel: sendFilesToModel.value }),
    })
    if (!response.ok) throw new Error(await response.text())
    const data = await response.json()
    messages.value.push({ role: 'assistant', text: data.answer, sources: data.sources ?? [] })
    if (avatarActive.value) await speak(data.answer)
  } catch (error) {
    messages.value.push({ role: 'assistant', text: `Chat failed: ${error instanceof Error ? error.message : 'Unknown error'}` })
  } finally {
    chatBusy.value = false
  }
}

async function startAvatar() {
  if (avatarActive.value || avatarBusy.value) return
  avatarBusy.value = true
  try {
    speechSdk = await loadSpeechSdk()
    const tokenResponse = await fetch(`${apiBase}/api/avatar/token?avatarCharacter=${selectedAvatarCharacter.value}&avatarStyle=${selectedAvatarStyle.value}`)
    if (!tokenResponse.ok) throw new Error(await tokenResponse.text())
    const token = await tokenResponse.json()

    const relayResponse = await fetch(`${apiBase}/api/avatar/relay-token`)
    if (!relayResponse.ok) throw new Error(await relayResponse.text())
    const relay = await relayResponse.json()

    peerConnection = new RTCPeerConnection({
      iceServers: [{
        urls: relay.Urls ?? relay.urls,
        username: relay.Username ?? relay.username,
        credential: relay.Password ?? relay.password,
      }],
    })

    peerConnection.ontrack = event => {
      if (avatarVideo.value) {
        avatarVideo.value.srcObject = event.streams[0]
        avatarVideo.value.play().catch(() => {})
      }
    }

    peerConnection.addTransceiver('video', { direction: 'sendrecv' })
    peerConnection.addTransceiver('audio', { direction: 'sendrecv' })

    const speechConfig = speechSdk.SpeechConfig.fromAuthorizationToken(token.token, token.region)
    speechConfig.speechSynthesisVoiceName = 'en-US-JennyNeural'

    const avatarConfig = new speechSdk.AvatarConfig(token.avatarCharacter, token.avatarStyle)
    avatarSynthesizer = new speechSdk.AvatarSynthesizer(speechConfig, avatarConfig)
    await avatarSynthesizer.startAvatarAsync(peerConnection)

    avatarActive.value = true
    await speak('I am ready. Upload your materials and ask me what to read or explain.')
    startContinuousListening()
  } catch (error) {
    messages.value.push({ role: 'assistant', text: `Avatar failed to start: ${error instanceof Error ? error.message : 'Unknown error'}` })
  } finally {
    avatarBusy.value = false
  }
}

async function stopAvatar() {
  stopListening()
  avatarSynthesizer?.close?.()
  peerConnection?.close()
  avatarSynthesizer = null
  peerConnection = null
  avatarActive.value = false
}

async function speak(text: string) {
  if (!avatarSynthesizer || !text.trim()) return
  await avatarSynthesizer.speakTextAsync(text.slice(0, 2500))
}


async function startContinuousListening() {
  if (listening.value) return
  listening.value = true
  try {
    const sdk = speechSdk ?? await loadSpeechSdk()
    const tokenResponse = await fetch(`${apiBase}/api/avatar/token?avatarCharacter=${selectedAvatarCharacter.value}&avatarStyle=${selectedAvatarStyle.value}`)
    if (!tokenResponse.ok) throw new Error(await tokenResponse.text())
    const token = await tokenResponse.json()

    const speechConfig = sdk.SpeechConfig.fromAuthorizationToken(token.token, token.region)
    speechConfig.speechRecognitionLanguage = 'en-US'
    const audioConfig = sdk.AudioConfig.fromDefaultMicrophoneInput()
    speechRecognizer = new sdk.SpeechRecognizer(speechConfig, audioConfig)

    speechRecognizer.recognized = (_: any, e: any) => {
      if (e.result.reason === sdk.ResultReason.RecognizedSpeech && e.result.text?.trim()) {
        prompt.value = e.result.text
        ask()
      }
    }

    speechRecognizer.canceled = (_: any, e: any) => {
      listening.value = false
      speechRecognizer = null
      if (e?.errorDetails) {
        messages.value.push({ role: 'assistant', text: `Mic stopped: ${e.errorDetails}` })
      }
    }

    speechRecognizer.startContinuousRecognitionAsync(
      () => { /* started successfully */ },
      (err: any) => {
        listening.value = false
        speechRecognizer = null
        messages.value.push({ role: 'assistant', text: `Mic failed to start: ${err}` })
      }
    )
  } catch (error) {
    listening.value = false
    messages.value.push({ role: 'assistant', text: `Microphone error: ${error instanceof Error ? error.message : 'Unknown error'}` })
  }
}

function stopListening() {
  speechRecognizer?.stopContinuousRecognitionAsync()
  speechRecognizer = null
  listening.value = false
}

async function loadSpeechSdk() {
  if ((window as any).SpeechSDK) return (window as any).SpeechSDK
  return new Promise((resolve, reject) => {
    const script = document.createElement('script')
    script.src = 'https://aka.ms/csspeech/jsbrowserpackageraw'
    script.onload = () => resolve((window as any).SpeechSDK)
    script.onerror = () => reject(new Error('Failed to load Azure Speech SDK'))
    document.head.appendChild(script)
  })
}

refreshLibrary()
</script>

<template>
  <main class="shell">
    <section class="hero">
      <div>
        <p class="eyebrow">Avatar Knowledge Room</p>
        <h1>Feed it files, folders, images, audio, or video. Let the avatar work with you.</h1>
      </div>
      <div class="status-strip">
        <span>{{ library.length }} items</span>
        <span>{{ stats.text }} text</span>
        <span>{{ stats.pdf }} PDFs</span>
        <span>{{ stats.image }} images</span>
        <span>{{ stats.audio }} audio</span>
        <span>{{ stats.video }} videos</span>
      </div>
    </section>

    <section class="workspace">
      <div class="stage-card">
        <video ref="avatarVideo" class="avatar-video" autoplay playsinline></video>
        <div v-if="!avatarActive" class="avatar-empty">
          <div class="avatar-mark">{{ selectedAvatar.label.slice(0, 2).toUpperCase() }}</div>
          <strong>Avatar ready to start</strong>
          <span>Choose an avatar, upload content, then start the room.</span>
        </div>
      </div>

      <aside class="side-panel">
        <div class="panel-card">
          <h2>Avatar</h2>
          <div class="field">
            <label>Character</label>
            <select v-model="selectedAvatarCharacter" :disabled="avatarActive" @change="onAvatarChange">
              <option v-for="avatar in avatarOptions" :key="avatar.character" :value="avatar.character">
                {{ avatar.label }} - {{ avatar.gender }}
              </option>
            </select>
          </div>
          <div class="field">
            <label>Style</label>
            <select v-model="selectedAvatarStyle" :disabled="avatarActive">
              <option v-for="style in selectedAvatarStyles" :key="style" :value="style">
                {{ formatStyle(style) }}
              </option>
            </select>
          </div>
          <div class="button-row">
            <button v-if="!avatarActive" class="primary" :disabled="avatarBusy" @click="startAvatar">
              {{ avatarBusy ? 'Starting...' : 'Start Avatar' }}
            </button>
            <button v-else class="danger" @click="stopAvatar">Stop Avatar</button>
            <button v-if="avatarActive" class="mic-toggle" :class="listening ? 'mic-on' : 'mic-off'" @click="listening ? stopListening() : startContinuousListening()" :title="listening ? 'Mic on – click to mute' : 'Mic off – click to enable'">
              <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
                <path d="M12 14a3 3 0 0 0 3-3V5a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V20H9v2h6v-2h-2v-2.08A7 7 0 0 0 19 11h-2z"/>
              </svg>
            </button>
          </div>
        </div>

        <div class="panel-card">
          <h2>Library</h2>
          <div class="upload-grid">
            <label class="upload-tile">
              <input type="file" multiple @change="uploadFiles">
              <strong>Upload files</strong>
              <span>Docs, images, audio, videos</span>
            </label>
            <label class="upload-tile">
              <input type="file" multiple webkitdirectory directory @change="uploadFiles">
              <strong>Upload folder</strong>
              <span>Keeps relative paths</span>
            </label>
          </div>
          <button class="ghost full" :disabled="uploadBusy || library.length === 0" @click="clearLibrary">
            {{ uploadBusy ? 'Importing...' : 'Clear library' }}
          </button>
          <div class="library-list">
            <div v-for="item in library.slice(0, 8)" :key="item.id" class="library-item">
              <span :class="['kind', item.kind]">{{ item.kind }}</span>
              <div>
                <strong>{{ item.name }}</strong>
                <small>{{ item.path }}</small>
              </div>
            </div>
          </div>
        </div>
      </aside>
    </section>

    <section class="chat-card">
      <div class="chat-log">
        <article v-for="(message, index) in messages" :key="index" :class="['message', message.role]">
          <strong>{{ message.role === 'user' ? 'You' : 'Avatar' }}</strong>
          <p>{{ message.text }}</p>
          <div v-if="message.sources?.length" class="sources">
            <span v-for="source in message.sources" :key="source.id">{{ source.name }}</span>
          </div>
        </article>
      </div>
      <form class="prompt-row" @submit.prevent="ask">
        <label class="model-toggle">
          <input v-model="sendFilesToModel" type="checkbox" />
          <span>Send PDFs/images to model</span>
        </label>
        <input v-model="prompt" placeholder="Ask it to summarize, read, compare, explain, or find something..." />
        <button class="primary" :disabled="chatBusy">{{ chatBusy ? 'Thinking...' : 'Ask' }}</button>
      </form>
    </section>
  </main>
</template>
