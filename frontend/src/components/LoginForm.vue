<template>
  <div class="login-form">
    <h3>登录</h3>
    <input v-model="username" placeholder="用户名" @keyup.enter="login" />
    <input v-model="password" type="password" placeholder="密码" @keyup.enter="login" />
    <button @click="login" :disabled="loading">{{ loading ? '登录中...' : '登录' }}</button>
    <p v-if="error" class="error">{{ error }}</p>
  </div>
</template>

<script>
export default {
  name: 'LoginForm',
  data() {
    return { username: '', password: '', loading: false, error: '' }
  },
  methods: {
    async login() {
      if (!this.username || !this.password) {
        this.error = '请输入用户名和密码'
        return
      }
      this.loading = true
      this.error = ''
      try {
        const res = await fetch('/api/customer/auth/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ username: this.username, password: this.password })
        })
        const data = await res.json()
        if (data.success) {
          this.$emit('login', { username: data.username, token: data.token })
        } else {
          this.error = data.message || '登录失败'
        }
      } catch {
        this.error = '连接失败'
      } finally {
        this.loading = false
      }
    }
  }
}
</script>
