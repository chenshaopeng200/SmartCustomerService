<template>
  <div class="app-container">
    <header class="app-header">
      <h1>智能客服</h1>
      <div class="header-actions">
        <span v-if="username" class="username">{{ username }}</span>
        <button @click="showEval = !showEval" class="btn-eval">
          {{ showEval ? '返回聊天' : '评估面板' }}
        </button>
        <button v-if="!username" @click="showLogin = !showLogin" class="btn-login">
          {{ showLogin ? '返回聊天' : '登录' }}
        </button>
        <button v-if="username" @click="logout" class="btn-logout">退出</button>
      </div>
    </header>

    <LoginForm v-if="showLogin" @login="onLogin" />

    <EvalPanel v-else-if="showEval" :username="username" />

    <ChatWindow v-else :messages="messages" :streaming="streaming" />

    <ChatInput
      v-if="!showLogin && !showEval"
      :disabled="streaming"
      @send="onSend"
    />
  </div>
</template>

<script>
import ChatWindow from './components/ChatWindow.vue'
import ChatInput from './components/ChatInput.vue'
import EvalPanel from './components/EvalPanel.vue'
import LoginForm from './components/LoginForm.vue'

export default {
  name: 'App',
  components: { ChatWindow, ChatInput, EvalPanel, LoginForm },
  data() {
    let username = ''
    let token = ''
    try {
      username = localStorage.getItem('username') || ''
      token = localStorage.getItem('token') || ''
    } catch (e) {
      // localStorage not available
    }
    return {
      messages: [],
      streaming: false,
      username,
      token,
      showEval: false,
      showLogin: false,
      debug: 'ready'
    }
  },
  mounted() {
    console.log('App mounted, messages:', this.messages.length)
  },
  methods: {
    onLogin({ username, token }) {
      this.username = username
      this.token = token
      try {
        localStorage.setItem('username', username)
        localStorage.setItem('token', token)
      } catch (e) {}
      this.showLogin = false
    },
    logout() {
      this.username = ''
      this.token = ''
      try {
        localStorage.removeItem('username')
        localStorage.removeItem('token')
      } catch (e) {}
      this.messages = []
    },
    async onSend(message) {
      const userMsg = { role: 'user', content: message, citations: [] }
      const assistantMsg = { role: 'assistant', content: '', citations: [] }
      this.messages.push(userMsg)
      this.messages.push(assistantMsg)
      this.streaming = true
      this.debug = 'sending'

      try {
        const headers = { 'Content-Type': 'application/json' }
        if (this.token) headers['Authorization'] = `Bearer ${this.token}`

        const response = await fetch('/api/customer/chat/stream', {
          method: 'POST',
          headers,
          body: JSON.stringify({
            userId: this.username || 'anonymous',
            message
          })
        })

        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        if (!response.body) throw new Error('Response body is null')

        const reader = response.body.getReader()
        const decoder = new TextDecoder()
        let buffer = ''
        let idx = this.messages.length - 1

        while (true) {
          const { done, value } = await reader.read()
          if (done) break
          buffer += decoder.decode(value, { stream: true })
          const lines = buffer.split('\n')
          buffer = lines.pop() || ''

          for (const line of lines) {
            if (!line.startsWith('data: ')) continue
            const data = line.slice(6)
            if (data === '[DONE]') continue
            if (data.startsWith('[ERROR]')) {
              this.messages[idx].content = data.replace('[ERROR] ', '')
              continue
            }
            this.messages[idx].content += data
          }
        }

        this.debug = 'done'
        this.highlightCitations(idx)
      } catch (err) {
        const lastIdx = this.messages.length - 1
        this.messages[lastIdx].content = '连接失败：' + err.message
        this.debug = 'error: ' + err.message
      } finally {
        this.streaming = false
      }
    },
    highlightCitations(idx) {
      const msg = this.messages[idx]
      if (!msg) return
      const matches = msg.content.match(/\[\d+\]/g)
      if (matches) msg.citations = [...new Set(matches)]
    }
  }
}
</script>
