/// 4-pt spacing grid per `docs/Standards/DESIGN_SYSTEM.md` L226-240.
/// Views stack with `StackPanel.spacing Spacing.md` and pad with
/// `Border.padding (Thickness Spacing.lg)`. No magic numbers in the
/// view DSL — if a value isn't in the scale, propose adding it here
/// rather than inlining a literal.
module Stem.ButtonPanelTester.GUI.Spacing

let xs   = 4.0      // hairline padding, tight stacks
let sm   = 8.0      // standard control padding
let md   = 12.0     // grouped-control spacing
let lg   = 16.0     // section padding
let xl   = 24.0     // page margins
let xxl  = 32.0     // page-section breaks
let xxxl = 56.0     // hero / dialog padding
let huge = 80.0     // full-bleed splash
