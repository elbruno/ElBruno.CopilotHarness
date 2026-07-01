# Image Generation Prompts for ElBruno.CopilotHarness
## Ready-to-run t2i prompts

> **Note:** The `t2i` CLI is installed and available (`t2i --help` works) but **no cloud provider is configured** (0/3 credentials for all providers). These prompts are staged for Bruno to run once a provider endpoint and API key are set up.
>
> To configure: `t2i config` — then run each prompt below with the suggested command.

---

## Image 1 — Social Card / Open Graph (1200 × 630)

**Filename:** `docs/assets/social-card.png`

**Prompt:**
```
A clean, dark-themed technical diagram showing a vertical pipeline of five labeled boxes connected by glowing downward arrows. The boxes represent layers of a software architecture: at top "GitHub Copilot" (blue glow), then "BYOK Config" (purple glow), then "Router / Aspire" (green glow), then "Model Selector" (orange glow), then at bottom two side-by-side boxes "Local LLM" and "Azure OpenAI" (amber glow). Background is deep dark navy (#0d1117). The style is modern developer-focused UI illustration, flat but with subtle neon glows and thin borders. Wide aspect ratio, minimal, no text watermark. Professional and clean.
```

**t2i command:**
```powershell
t2i "A clean, dark-themed technical diagram showing a vertical pipeline of five labeled boxes connected by glowing downward arrows. The boxes represent layers of a software architecture: at top GitHub Copilot (blue glow), then BYOK Config (purple glow), then Router / Aspire (green glow), then Model Selector (orange glow), then at bottom two side-by-side boxes Local LLM and Azure OpenAI (amber glow). Background is deep dark navy. The style is modern developer-focused UI illustration, flat but with subtle neon glows and thin borders. Wide aspect ratio, minimal, professional and clean." --provider foundry-flux2 -w 1200 --height 630 -o docs/assets/social-card.png
```

**Suggested use:** Open Graph / Twitter card image, blog post hero image, README social preview.

---

## Image 2 — Hero / Presentation Background (1600 × 900)

**Filename:** `docs/assets/hero-bg.png`

**Prompt:**
```
Dark atmospheric background illustration for a developer conference presentation slide. Abstract layered architecture theme: faint glowing horizontal bands flowing downward suggesting data routing through pipeline layers, in deep navy and dark charcoal with subtle blue and purple neon accents. Minimal, no text, no logos. Works as a slide background behind white text. Cinematic, moody, tech aesthetic. 16:9 aspect ratio.
```

**t2i command:**
```powershell
t2i "Dark atmospheric background illustration for a developer conference presentation slide. Abstract layered architecture theme: faint glowing horizontal bands flowing downward suggesting data routing through pipeline layers, in deep navy and dark charcoal with subtle blue and purple neon accents. Minimal, no text, no logos. Works as a slide background behind white text. Cinematic, moody, tech aesthetic." --provider foundry-flux2 -w 1600 --height 900 -o docs/assets/hero-bg.png
```

**Suggested use:** Background for the title slide of `docs/presentation/harness-layers.html`. Add as a CSS `background-image` on `.slide` (with a dark overlay for text readability).

---

## Referencing images in the blog post

Once generated, add to `docs/promo/blog-post.md` directly after the title:

```markdown
![ElBruno.CopilotHarness — 5-layer routing pipeline](../assets/social-card.png)
```

And update the presentation title slide (`slide-0`) with:

```html
<style>
  #slide-0 { background-image: url('../../assets/hero-bg.png'); background-size: cover; }
  #slide-0::before { content:''; position:absolute; inset:0; background:rgba(13,17,23,0.75); border-radius:var(--radius); }
</style>
```
