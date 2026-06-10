<template>
  <div :class="['message-bubble', message.role]">
    <div class="role-label">{{ message.role === 'user' ? '你' : '客服' }}</div>
    <div class="content" v-html="renderContent(message.content)"></div>
    <div v-if="message.citations?.length" class="citations">
      引用: {{ message.citations.join(', ') }}
    </div>
  </div>
</template>

<script>
export default {
  name: 'MessageBubble',
  props: { message: Object },
  methods: {
    renderContent(text) {
      return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/\[(\d+)\]/g, '<sup class="cite">[$1]</sup>')
        .replace(/\n/g, '<br>')
    }
  }
}
</script>
