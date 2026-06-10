<template>
  <div class="chat-window" ref="window">
    <div v-if="messages.length === 0" class="empty-state">
      输入问题开始对话
    </div>
    <MessageBubble
      v-for="(msg, idx) in messages"
      :key="idx"
      :message="msg"
    />
    <div v-if="streaming && messages[messages.length - 1]?.content === ''" class="typing-indicator">
      <span></span><span></span><span></span>
    </div>
  </div>
</template>

<script>
import MessageBubble from './MessageBubble.vue'

export default {
  name: 'ChatWindow',
  components: { MessageBubble },
  props: {
    messages: Array,
    streaming: Boolean
  },
  watch: {
    messages: {
      handler() {
        this.$nextTick(() => {
          const el = this.$refs.window
          if (el) el.scrollTop = el.scrollHeight
        })
      },
      deep: true
    }
  }
}
</script>
