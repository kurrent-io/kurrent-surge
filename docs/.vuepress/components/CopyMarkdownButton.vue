<template>
    <button
        class="copy-md-btn"
        type="button"
        :disabled="loading || !available"
        :aria-busy="loading"
        @click="copy"
    >
        <svg
            v-if="copied"
            class="copy-md-btn__icon"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
        >
            <path d="M20 6 9 17l-5-5" />
        </svg>
        <svg
            v-else
            class="copy-md-btn__icon"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
        >
            <rect x="8" y="8" width="14" height="14" rx="2" ry="2" />
            <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
        </svg>
        <span v-if="loading">Copying…</span>
        <span v-else-if="copied">Copied</span>
        <span v-else>Copy Markdown</span>
    </button>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import { usePageData } from "vuepress/client";

// Lazy-loaded raw markdown sources, bundled at build time.
// Keys are glob paths relative to this component file.
const markdownFiles = import.meta.glob<string>(
    ["../../client/**/*.md", "../../processor/**/*.md"],
    { query: "?raw", import: "default" }
);

const page = usePageData();
const loading = ref(false);
const copied = ref(false);

const lookupKey = computed(() => {
    const rel = page.value.filePathRelative;
    if (!rel) return null;
    return Object.keys(markdownFiles).find((k) => k.endsWith("/" + rel)) ?? null;
});

const available = computed(() => lookupKey.value !== null);

async function copy() {
    const key = lookupKey.value;
    if (!key || loading.value) return;

    loading.value = true;
    try {
        const content = await markdownFiles[key]();
        await navigator.clipboard.writeText(content);
        copied.value = true;
        setTimeout(() => { copied.value = false; }, 1500);
    } catch (err) {
        console.error("Copy as markdown failed:", err);
    } finally {
        loading.value = false;
    }
}
</script>

<style scoped>
.copy-md-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;

    padding: 0.4rem 0.85rem;
    margin: 0 0 1.5rem;

    border-radius: 0.375rem;
    border: 1px solid var(--vp-c-border, #d1d5db);
    background: var(--vp-c-bg, #fff);
    color: var(--vp-c-text, #1f2937);

    font-family: inherit;
    font-size: 0.8rem;
    font-weight: 500;
    line-height: 1;
    cursor: pointer;

    transition:
        background 0.15s ease,
        border-color 0.15s ease,
        color 0.15s ease;
}

.copy-md-btn:hover:not(:disabled) {
    background: var(--vp-c-bg-alt, #f9fafb);
    border-color: var(--vp-c-accent, #6366f1);
    color: var(--vp-c-accent, #6366f1);
}

.copy-md-btn:disabled {
    opacity: 0.6;
    cursor: not-allowed;
}

.copy-md-btn__icon {
    width: 0.95rem;
    height: 0.95rem;
    flex-shrink: 0;
}
</style>
