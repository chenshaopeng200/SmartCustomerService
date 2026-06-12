<template>
  <div class="eval-panel">
    <h3>RAG 评估面板</h3>

    <div class="eval-section">
      <h4>自动评估</h4>
      <input v-model="evalQuery" placeholder="输入测试问题" @keyup.enter="runEval" />
      <button @click="runEval" :disabled="evalLoading">{{ evalLoading ? '评估中...' : '评估' }}</button>
      <div v-if="evalResult" class="eval-result">
        <div class="scores">
          <span class="score">忠实度: {{ evalResult.faithfulness }}</span>
          <span class="score">相关性: {{ evalResult.relevance }}</span>
          <span class="score">检索精度: {{ evalResult.retrievalPrecision }}</span>
          <span class="score overall">综合: {{ evalResult.overallScore }}</span>
        </div>
        <pre class="details">{{ evalResult.details }}</pre>
      </div>
    </div>

    <div class="eval-section">
      <h4>A/B 对比测试</h4>
      <input v-model="compareQuery" placeholder="输入测试问题" @keyup.enter="runCompare" />
      <div class="config-grid">
        <div>
          <label>配置 A (完整流水线)</label>
        </div>
        <div>
          <label>配置 B (基础检索)</label>
        </div>
      </div>
      <button @click="runCompare" :disabled="compareLoading">{{ compareLoading ? '对比中...' : '对比' }}</button>
      <div v-if="compareResult" class="compare-result">
        <h4>{{ compareResult.winner }}</h4>
        <p>{{ compareResult.analysis }}</p>
        <div class="side-by-side">
          <div class="side">
            <strong>A: {{ compareResult.sideA.scores.overallScore }}</strong>
            <div class="answer-preview">{{ compareResult.sideA.answer?.substring(0, 200) }}...</div>
          </div>
          <div class="side">
            <strong>B: {{ compareResult.sideB.scores.overallScore }}</strong>
            <div class="answer-preview">{{ compareResult.sideB.answer?.substring(0, 200) }}...</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: 'EvalPanel',
  props: { username: String },
  data() {
    return {
      evalQuery: '',
      evalLoading: false,
      evalResult: null,
      compareQuery: '',
      compareLoading: false,
      compareResult: null
    }
  },
  methods: {
    async runEval() {
      if (!this.evalQuery.trim()) return
      this.evalLoading = true
      this.evalResult = null
      try {
        const res = await fetch('/api/customer/eval', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ query: this.evalQuery, userId: this.username || 'anonymous' })
        })
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        this.evalResult = await res.json()
      } catch (err) {
        this.evalResult = {
          faithfulness: 0,
          relevance: 0,
          retrievalPrecision: 0,
          overallScore: 0,
          details: '评估失败: ' + err.message
        }
      } finally {
        this.evalLoading = false
      }
    },
    async runCompare() {
      if (!this.compareQuery.trim()) return
      this.compareLoading = true
      this.compareResult = null
      try {
        const res = await fetch('/api/customer/eval/compare', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ query: this.compareQuery })
        })
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        this.compareResult = await res.json()
      } catch (err) {
        this.compareResult = {
          query: this.compareQuery,
          winner: '评估失败',
          analysis: '对比失败: ' + err.message,
          sideA: { label: 'A', config: {}, answer: '', sources: [], scores: { faithfulness: 0, relevance: 0, retrievalPrecision: 0, overallScore: 0 } },
          sideB: { label: 'B', config: {}, answer: '', sources: [], scores: { faithfulness: 0, relevance: 0, retrievalPrecision: 0, overallScore: 0 } }
        }
      } finally {
        this.compareLoading = false
      }
    }
  }
}
</script>
