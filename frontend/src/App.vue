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
    return {
      messages: [],
      streaming: false,
      username: localStorage.getItem('username') || '',
      token: localStorage.getItem('token') || '',
      showEval: false,
      showLogin: false
    }
  },
  methods: {
    onLogin({ username, token }) {
      this.username = username
      this.token = token
      localStorage.setItem('username', username)
      localStorage.setItem('token', token)
      this.showLogin = false
    },
    logout() {
      this.username = ''
      this.token = ''
      localStorage.removeItem('username')
      localStorage.removeItem('token')
      this.messages = []
    },
    async onSend(message) {
      this.messages.push({ role: 'user', content: message, citations: [] })
      this.streaming = true

      const assistantMsg = { role: 'assistant', content: '', citations: [] }
      this.messages.push(assistantMsg)

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

        const reader = response.body.getReader()
        const decoder = new TextDecoder()
        let buffer = ''

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
              assistantMsg.content = data.replace('[ERROR] ', '')
              continue
            }
            assistantMsg.content += data
          }
        }

        this.highlightCitations(assistantMsg)
      } catch (err) {
        assistantMsg.content = '连接失败：' + err.message
      } finally {
        this.streaming = false
      }
    },
    highlightCitations(msg) {
      const matches = msg.content.match(/\[\d+\]/g)
      if (matches) msg.citations = [...new Set(matches)]
    }
  }
}
</script>
