# Emberfall UI alignment review

<!-- image-to-ui-review-binding: structure=2f29855f9f4efb623196a32623ad07af5ca48c2aa442582a271edfc769e0a57c artifacts=241e966988ace9ff48fb3a82926d6094152d1dfdfcfb2492ec298e9884206c72 -->

| Element path | Status | Review / change | Recheck |
| --- | --- | --- | --- |
| `root/background` | aligned | Native 1672×941 bounds match the canvas; the unsupplied hero scene layer is documented as an accepted approximation. | `comparison.png` |
| `root/top_left_hud` | aligned | Group bounds contain the portrait, status bars, and level plaque at the reference edges. | `all_elements.png` |
| `root/top_left_hud/status_bars` | aligned | Outer frame and both color-band boundaries align with the top-left status area. | `comparison.png` |
| `root/top_left_hud/portrait` | aligned | Composite portrait control bounds align; missing portrait artwork is documented separately. | `all_elements.png` |
| `root/top_left_hud/portrait/frame` | aligned | Circular frame bbox aligns; the dark center is intentional because no portrait sprite was supplied. | `comparison.png` |
| `root/top_left_hud/portrait/level_badge` | adjusted | Parent changed from position (25,156), size 140×47 to (27,156), 132×47 to match the reference plaque. | `all_elements.png` |
| `root/top_left_hud/portrait/level_badge/base` | adjusted | Base width changed from 140 to 132 while height stayed 47, tightening both horizontal edges. | `all_elements.png` |
| `root/top_left_hud/portrait/level_badge/label` | adjusted | Text box changed from (8,4), 124×39 to (6,9), 120×39 and font size 24→22 for ink alignment. | `risk_review.png` |
| `root/resource_bar` | aligned | Two repeated resource controls align as one 463×62 row with consistent centers. | `all_elements.png` |
| `root/resource_bar/coin` | aligned | Coin control outer bbox matches the first resource capsule. | `all_elements.png` |
| `root/resource_bar/coin/capsule` | aligned | Capsule frame matches the reference horizontal resource field. | `comparison.png` |
| `root/resource_bar/coin/frame` | aligned | Round frame is centered on the capsule's left edge. | `all_elements.png` |
| `root/resource_bar/coin/icon` | aligned | Coin glyph keeps its source aspect and is centered inside the round frame. | `comparison.png` |
| `root/resource_bar/crystal` | aligned | Crystal control outer bbox matches the second resource capsule. | `all_elements.png` |
| `root/resource_bar/crystal/capsule` | aligned | Capsule frame matches the reference horizontal resource field. | `comparison.png` |
| `root/resource_bar/crystal/frame` | aligned | Round frame is centered on the capsule's left edge. | `all_elements.png` |
| `root/resource_bar/crystal/icon` | aligned | Crystal glyph keeps its source aspect and is centered inside the round frame. | `comparison.png` |
| `root/top_right_utilities` | aligned | Settings and mail controls align as a two-item row at the upper-right margin. | `all_elements.png` |
| `root/top_right_utilities/settings` | aligned | Settings composite matches the reference control bbox. | `all_elements.png` |
| `root/top_right_utilities/settings/base` | aligned | Utility round base aligns with the visible outer ring. | `comparison.png` |
| `root/top_right_utilities/settings/icon` | aligned | Gear glyph is centered and preserves its atomic aspect ratio. | `all_elements.png` |
| `root/top_right_utilities/mail` | aligned | Mail composite matches the reference control bbox. | `all_elements.png` |
| `root/top_right_utilities/mail/base` | aligned | Utility round base aligns with the visible outer ring. | `comparison.png` |
| `root/top_right_utilities/mail/icon` | aligned | Mail glyph is centered and preserves its atomic aspect ratio. | `risk_review.png` |
| `root/quest_panel` | aligned | Panel parent matches the active quest surface and contains all visible children. | `all_elements.png` |
| `root/quest_panel/base` | aligned | Nine-sliced panel edges, crest, and lower ornament align with the reference panel. | `comparison.png` |
| `root/quest_panel/title` | adjusted | Text box moved from (80,53) to (85,59); current ink is centered under the crest. | `comparison.png` |
| `root/quest_panel/divider` | aligned | Divider spans the header at the reference vertical band. | `all_elements.png` |
| `root/quest_panel/quest_list` | aligned | Two-row column bounds and repeated center spacing match the reference list. | `all_elements.png` |
| `root/quest_panel/quest_list/fire_quest` | aligned | First quest row outer control matches the fire-row reference bbox. | `all_elements.png` |
| `root/quest_panel/quest_list/fire_quest/background` | adjusted | Background changed from (13,10), 424×108 to (19,6), 425×108 so its inner edges match the fire card frame. | `target_root_quest_panel_quest_list_fire_quest_background.png` |
| `root/quest_panel/quest_list/fire_quest/frame` | aligned | Nine-sliced row frame preserves corners and aligns with the row edges. | `all_elements.png` |
| `root/quest_panel/quest_list/fire_quest/action_base` | aligned | Round action base overlaps the row's left edge at the reference center. | `all_elements.png` |
| `root/quest_panel/quest_list/fire_quest/icon` | aligned | Fire sigil keeps its source aspect and is centered in the action base. | `comparison.png` |
| `root/quest_panel/quest_list/frost_quest` | aligned | Second quest row outer control matches the frost-row reference bbox. | `all_elements.png` |
| `root/quest_panel/quest_list/frost_quest/background` | adjusted | Background changed from (13,10), 424×108 to (19,6), 425×108 so its inner edges match the frost card frame. | `target_root_quest_panel_quest_list_frost_quest_background.png` |
| `root/quest_panel/quest_list/frost_quest/frame` | aligned | Nine-sliced row frame preserves corners and aligns with the row edges. | `all_elements.png` |
| `root/quest_panel/quest_list/frost_quest/action_base` | aligned | Round action base overlaps the row's left edge at the reference center. | `all_elements.png` |
| `root/quest_panel/quest_list/frost_quest/icon` | aligned | Frozen-skull glyph keeps its source aspect and is centered in the action base. | `comparison.png` |
| `root/right_actions` | aligned | Three-button column and overlap spacing match the right-side action rail. | `all_elements.png` |
| `root/right_actions/sword` | aligned | Sword control matches the first repeated action bbox. | `all_elements.png` |
| `root/right_actions/sword/base` | aligned | Outer action frame aligns with the first visible circular control. | `all_elements.png` |
| `root/right_actions/sword/icon` | aligned | Sword glyph keeps its source aspect and is centered in the frame. | `comparison.png` |
| `root/right_actions/helmet` | aligned | Helmet control matches the second repeated action bbox. | `all_elements.png` |
| `root/right_actions/helmet/base` | aligned | Outer action frame aligns with the second visible circular control. | `all_elements.png` |
| `root/right_actions/helmet/icon` | aligned | Helmet glyph keeps its source aspect and is centered in the frame. | `comparison.png` |
| `root/right_actions/spectral_flame` | aligned | Spectral-flame control matches the third repeated action bbox. | `all_elements.png` |
| `root/right_actions/spectral_flame/base` | aligned | Outer action frame aligns with the third visible circular control. | `all_elements.png` |
| `root/right_actions/spectral_flame/icon` | aligned | Flame glyph keeps its source aspect and is centered in the frame. | `comparison.png` |
| `root/bottom_navigation` | aligned | Dock and button row share the measured bottom-center parent bounds. | `all_elements.png` |
| `root/bottom_navigation/dock` | aligned | Dock frame spans the five navigation centers and aligns with the reference baseline. | `comparison.png` |
| `root/bottom_navigation/nav_buttons` | aligned | Five-item layout matches the measured control centers after final offsets. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/equipment` | adjusted | Size changed 132×134→126×128 and offset changed to (3,6), matching the first target bbox. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/equipment/base` | adjusted | Base changed 132×134→126×128 and inherits the adjusted parent center. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/equipment/icon` | adjusted | Position changed (36,23)→(33,14), retaining the reference absolute glyph bbox after the parent move. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/armor` | adjusted | Size changed 132×134→126×128 and offset changed to (-4,6), matching the second target bbox. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/armor/base` | adjusted | Base changed 132×134→126×128 and inherits the adjusted parent center. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/armor/icon` | adjusted | Position changed (37,33)→(41,24), retaining the reference absolute glyph bbox after the parent move. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/talents` | adjusted | Parent offset changed from (0,0) to (7,0), centering the enlarged active control. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/talents/base` | adjusted | Base inherits the parent's +7 px horizontal correction; size remains 170×173. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/talents/icon` | adjusted | Glyph inherits the parent's +7 px correction; recoloring remains an accepted asset approximation. | `risk_review.png` |
| `root/bottom_navigation/nav_buttons/spellbook` | adjusted | Size changed 132×134→126×128 and offset changed to (4,6), matching the fourth target bbox. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/spellbook/base` | adjusted | Base changed 132×134→126×128 and inherits the adjusted parent center. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/spellbook/icon` | adjusted | Position changed (28,35)→(18,26), retaining the reference absolute glyph bbox after the parent move. | `risk_review.png` |
| `root/bottom_navigation/nav_buttons/map` | adjusted | Size changed 132×134→126×128 and offset changed to (-2,6), matching the fifth target bbox. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/map/base` | adjusted | Base changed 132×134→126×128 and inherits the adjusted parent center. | `all_elements.png` |
| `root/bottom_navigation/nav_buttons/map/icon` | adjusted | Position changed (31,32)→(27,23), retaining the reference absolute glyph bbox after the parent move. | `all_elements.png` |
| `root/enter_button` | adjusted | Parent changed from (1227,676), 445×239 to (1199,668), 465×253 to restore the reference right margin and visible height. | `target_root_enter_button.png` |
| `root/enter_button/base` | adjusted | Base changed 445×239→465×253; its visible alpha now aligns with the reference outer ornament. | `comparison.png` |
| `root/enter_button/label` | adjusted | Relative text position changed (40,69)→(65,77), preserving the corrected absolute word center and baseline after the parent resize. | `risk_review.png` |
