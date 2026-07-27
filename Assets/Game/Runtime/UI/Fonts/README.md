# Stage01 UI font

`RuleforgeStageOne.ttf` is a static, glyph-subset build of Noto Sans KR
Medium. It contains the ASCII, punctuation, Korean glyphs currently used by
`stage01-ko.json`, and the non-localized runtime symbols declared by
`StageOneUiFontCoverage`, keeping the WebGL payload small.

Source:
`https://github.com/google/fonts/tree/main/ofl/notosanskr`

The font remains licensed under the SIL Open Font License 1.1. The complete
license is included in `OFL.txt`. Regenerate the subset whenever Stage01
localization introduces new glyphs. Scene installation and PlayMode coverage
tests validate the entire localization file plus the runtime symbol set, rather
than checking Hangul alone.
