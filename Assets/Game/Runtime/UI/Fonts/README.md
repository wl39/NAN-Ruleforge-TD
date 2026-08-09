# Ruleforge pixel UI fonts

`RuleforgeStageOne.ttf` is a static, glyph-subset build of Galmuri11, a
Korean pixel font based on a 12-pixel bitmap grid. It contains the ASCII,
punctuation, Korean glyphs currently used by
`stage01-ko.json`, the TestLab corpus declared by `TestLabUiFontCoverage`, and
the non-localized runtime symbols declared by `StageOneUiFontCoverage`,
keeping the WebGL payload small.

The current subset includes the Stage01 localization, the non-localized
TestLab control-panel glyph corpus, all card names and descriptions through
content version 5, and the enemy inspection panel's Korean metadata.

Source:
`https://github.com/quiple/galmuri`

The font remains licensed under the SIL Open Font License 1.1. The complete
license is included in `OFL.txt`. Regenerate the subset whenever Stage01
localization introduces new glyphs. Scene installation and PlayMode coverage
tests validate the entire localization file plus the runtime symbol set, rather
than checking Hangul alone.

`RuleforgeCampaign.ttf` is a separate Galmuri11 subset for the
main-title and fifteen-node campaign-map localization. Keeping it separate
prevents campaign copy additions from inflating or silently changing the
battle HUD font. It is regenerated from the same Galmuri OFL source whenever
`MainMenuKo.json` adds visible characters.

Keep UI text sizes on whole pixels. Galmuri11 is designed around a 12-pixel
grid, so body text should not shrink below 12 pixels; larger display sizes
should preferably use integer or near-integer multiples of that grid.
