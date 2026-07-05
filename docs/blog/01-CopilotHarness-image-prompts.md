# Image Generation Prompts — Blog Post 01

Prompts usados para generar las imágenes con IA del blog post `01-CopilotHarness.md`.

**Herramienta:** `t2i` CLI  
**Provider:** `foundry-gpt-image-2` (modelo: `gpt-image-2`)  
**Resolución:** `1024×1024`

---

## hero.png

```
A developer at a modern desk with a laptop showing VS Code with GitHub Copilot.
Floating glowing orbs represent AI models - one labeled CLOUD fading in the
background, one labeled LOCAL glowing near the laptop. Dark blue tech aesthetic,
minimalist illustration, professional blog hero image
```

**Comando:**
```bash
t2i "A developer at a modern desk with a laptop showing VS Code with GitHub Copilot. \
Floating glowing orbs represent AI models - one labeled CLOUD fading in the \
background, one labeled LOCAL glowing near the laptop. Dark blue tech aesthetic, \
minimalist illustration, professional blog hero image" \
--provider foundry-gpt-image-2 \
--output docs/blog/01-CopilotHarness-images/hero.png \
--width 1024 --height 1024
```

---

## proxy-overview.png

```
Clean technical diagram showing three proxy boxes labeled OllamaProxy,
FoundryLocalProxy, FoundryProxy connected by arrows to a VS Code icon on the
left and different model backends on the right: Ollama local, Foundry Local
offline, Azure cloud. Dark background, neon connecting lines, flat tech
illustration style for a developer blog
```

**Comando:**
```bash
t2i "Clean technical diagram showing three proxy boxes labeled OllamaProxy, \
FoundryLocalProxy, FoundryProxy connected by arrows to a VS Code icon on the \
left and different model backends on the right: Ollama local, Foundry Local \
offline, Azure cloud. Dark background, neon connecting lines, flat tech \
illustration style for a developer blog" \
--provider foundry-gpt-image-2 \
--output docs/blog/01-CopilotHarness-images/proxy-overview.png \
--width 1024 --height 512
```

---

## byok-flow.png

```
Technical illustration showing the Copilot BYOK flow: a VS Code window with a
chatLanguageModels.json config file pointing with a glowing arrow to a local
proxy server icon, which points to a brain representing a local AI model. Labels
show endpoint URL and model name. Clean flat design, blue and teal color scheme,
developer documentation style
```

**Comando:**
```bash
t2i "Technical illustration showing the Copilot BYOK flow: a VS Code window with \
a chatLanguageModels.json config file pointing with a glowing arrow to a local \
proxy server icon, which points to a brain representing a local AI model. Labels \
show endpoint URL and model name. Clean flat design, blue and teal color scheme, \
developer documentation style" \
--provider foundry-gpt-image-2 \
--output docs/blog/01-CopilotHarness-images/byok-flow.png \
--width 1024 --height 512
```

---

## Notas

- Las imágenes de screenshots (`aspire-dashboard.png`, `aspire-traces.png`,
  `testapp-*.png`) se capturaron con `playwright-cli` de la app corriendo en local
  — no requieren prompt de generación.
- El provider `foundry-mai25` y `foundry-mai25-flash` dieron error con formato `png`
  en t2i v1.3.0 (`output_format: png` vs `image/png`). Se usó `foundry-gpt-image-2`
  como alternativa funcional.
- Si se regeneran las imágenes, ajustar `--width`/`--height` a múltiplos de 8.
