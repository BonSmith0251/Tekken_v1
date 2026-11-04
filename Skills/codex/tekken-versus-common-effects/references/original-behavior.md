# Establish original behavior before wiring

## Evidence has distinct jobs

Original pak properties establish assets and authored parameters. Original Blueprint/native code establishes dispatch only when the call path and arguments are recovered. Original execution establishes what happened in the recorded conditions. Unity code and screenshots establish the remaster's implementation. Do not substitute one for another or turn an unresolved field into a guessed default.

Keep a local record per investigated rule containing:

- Original build identity: executable SHA-256 for address-based analysis, archive selection and relevant package hashes. Record mods/settings and game mode; installed archives alone do not prove an unmodified retail environment.
- Binding: manager/default export, role, particle package/export, property offsets and raw values. Non-manager effects such as the KO Blueprint need their own binding evidence.
- Dispatch: call site/function plus callers and condition branches, or original capture/trace with exact frame/timestamp. Keep trigger condition, player ownership, effect argument, source time convention, transform basis, bone/socket and lifetime/stop condition separate. Mark each unresolved field explicitly.
- Observation: character, opponent, move/input, stage, facing, health/state, normal/counter/blocked result, mode and relevant display options. Retain raw video/trace and timestamps; do not keep only edited comparison images.
- Unity adaptation and result: unit/basis conversion, chosen fallback, unsupported modules, matched capture, remaining discrepancies. Include the tested conditions rather than claiming every variant is covered.

## Code and observation workflow

Start with the requested role's original binding, then inspect its native manager or referencing Blueprint. Resolve inherited functions and native calls rather than treating an empty Blueprint body as complete behavior. Trace callers and arguments far enough to distinguish contact, move-local events and practice-only displays. Record the executable hash and address basis (VA/RVA/module-relative); never reuse an address from another build without revalidation.

If runtime observation is needed, use the installed original game and existing local capture/telemetry tools after inspecting their scope. Do not use the Unity remake as the observation target. Compare one condition at a time: a whiff against contact; block against clean hit; normal against counter; hit at different health/damage; air landing against ordinary knockdown; KO against timeout. Check both owners/facings where ownership or transforms matter. These are investigation probes, not assumed native rules.

In particular, a punish reticle in one recording does not prove it belongs in ordinary versus: establish the original mode and display options. Worked example (2026-10-27): the blue ring with lens-flare needles at 54.9-55.5 s of `TEKKEN 7  2026-10-23 08-04-08.mp4` was wired as PracticePunishNotice, but its textures are Hwoarang's `T_EF_COL_hwo_light00_thk`/`T_EF_COL_hwo_line01_tss` (`PS_EF_CHR_HWO_nomal`, his character effect ID 0 on kicks). Compare the frame against each candidate's actual textures before naming the system. Note: the reference videos moved to `Videos/Captures/tk7/`. A particle's name or folder does not prove its gameplay role. Similar orange sparks do not identify an exact system without supporting binding/code/trace evidence. If a recording supports only visible timing or color, leave the exact asset identity unresolved.

For visual comparison, align first effect appearance and source action frame, identify the original camera/exposure settings, and compare lifetime, shape, scale, color, attachment and movement. Report tolerances or visible differences. Do not fit one screenshot by silently changing original spawn counts, dimensions or tints and then call the result exact.

## Integration decision

Wire a new automatic rule only for the conditions supported by original evidence. If attachment, timing or selection remains unresolved, continue extraction/analysis and offer a clearly labeled manual preview. Preserve existing unrelated runtime behavior; investigating uncertainty alone does not authorize disabling effects globally. If the user asks for a provisional implementation, label its assumptions in both conversion evidence and the result.

Report verdicts separately: **asset binding verified**, **original dispatch verified for stated conditions**, **Unity implementation tested**, **visual match tested**, or **unresolved**. The current skill helpers automate only asset consistency/binding checks. No helper's general `passed` flag certifies complete original battle behavior.
