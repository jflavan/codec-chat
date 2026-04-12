# FOSSA License Compliance Notes

## SkiaSharp Native Dependencies (5 issues)

**Status:** Approved — all flagged components have permissive license alternatives selected.

**Context:** Codec Chat is currently free and open-source. If the project is commercialized in the future, the license selections documented here remain fully compatible with commercial use, including proprietary distribution and SaaS deployment.

### Why FOSSA flags these

SkiaSharp bundles the native Skia graphics library, which includes several third-party components. Some of these components are **dual-licensed** under both permissive and copyleft terms. FOSSA detects the copyleft license text (GPL, LGPL) in the `THIRD-PARTY-NOTICES.txt` file and flags it automatically. However, dual-licensing means we **choose** which license to accept — we are selecting the permissive option in every case.

### Component-by-component analysis

| Component | Licenses offered | License we select | Commercial-safe | Obligations |
|-----------|-----------------|-------------------|-----------------|-------------|
| **Skia** | BSD-3-Clause | BSD-3-Clause | Yes | Include copyright notice |
| **FreeType** | FreeType License (FTL) **or** GPLv2 | FTL | Yes | Include FTL notice in docs |
| **HarfBuzz** | Old MIT | Old MIT | Yes | Include copyright notice |
| **libjpeg-turbo** | BSD-style (IJG, Modified BSD, zlib) | Modified BSD | Yes | Include copyright notice |
| **ICU** | Unicode License | Unicode License | Yes | Include copyright notice |
| **zlib** | zlib License | zlib License | Yes | None beyond not misrepresenting origin |
| **libpng** | libpng License | libpng License | Yes | Include copyright notice |

### What "permissive" means for future monetization

All selected licenses allow:
- Commercial use (selling, SaaS, subscriptions)
- Modification without disclosing source
- Proprietary distribution
- Sublicensing

The only obligation is to **include the copyright notices** in distribution (e.g., in an about/licenses page or bundled NOTICES file). These notices are provided in [`NOTICES.md`](../NOTICES.md) at the repository root. There is no copyleft obligation, no source disclosure requirement, and no restriction on charging for the product.

### What would change this assessment

- **Upgrading SkiaSharp** to a version that adds new native dependencies with copyleft-only licensing (no permissive alternative). Check THIRD-PARTY-NOTICES.txt on each upgrade.
- **Linking against system FreeType** compiled under GPLv2 instead of the bundled FTL-licensed version. The SkiaSharp NuGet packages bundle their own FTL-licensed copy, so this is not a concern with the current setup.

### Action required in FOSSA

Mark the 5 license compliance issues as **approved** with the rationale: "Dual-licensed components — permissive alternatives (FTL, BSD, MIT, zlib) selected per docs/FOSSA_LICENSE_NOTES.md."
