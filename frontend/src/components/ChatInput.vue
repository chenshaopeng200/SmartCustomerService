<template>
  <div class="chat-input">
    <textarea
      v-model="text"
      :disabled="disabled"
      placeholder="输入你的问题..."
      @keydown.enter.exact.prevent="send"
      rows="2"
    ></textarea>
    <button :disabled="disabled || !text.trim()" @click="send">发送</button>
  </div>
</template>

<script>
export default {
  name: 'ChatInput',
  props: { disabled: Boolean },
  data() {
    return { text: '' }
  },
  methods: {
    send() {
      const msg = this.text.trim()
      if (!msg) return
      this.$emit('send', msg)
      this.text = ''
    }
  }
}
</script>
