// Per-hero contact landmarks derived from the committed 12-frame original attack atlases.
// These are visual estimates, not Riot attack events or weapon collision volumes.
export const ATTACK_PROFILES=Object.freeze({
  '盖伦':Object.freeze({contactFrame:4,frameCount:12,range:180,halfWidth:42,confidence:'visual-estimate',note:'first sustained sword-down / forward-strike pose'}),
  '布里茨':Object.freeze({contactFrame:8,frameCount:12,range:165,halfWidth:52,confidence:'visual-estimate',note:'first fully extended punch pose'}),
  '锐雯':Object.freeze({contactFrame:4,frameCount:12,range:190,halfWidth:58,confidence:'visual-estimate',note:'first sustained horizontal slash / lunge pose'}),
  '亚索':Object.freeze({contactFrame:4,frameCount:12,range:185,halfWidth:46,confidence:'visual-estimate',note:'first sustained horizontal sword-strike pose'})
});

export function attackTimingFor(hero,clip){
  const profile=ATTACK_PROFILES[hero];
  if(!profile)throw new RangeError('Unsupported attack profile: '+hero);
  if(!clip||clip.frames!==profile.frameCount)throw new RangeError('Expected a 12-frame original attack clip');
  if(!Number.isFinite(clip.duration)||clip.duration<=0)throw new RangeError('Invalid attack clip duration');
  // Atlas frame N is displayed on [N-1, N) / frameCount of the clip.
  // Add a tiny deterministic epsilon so floating-point rounding cannot select the previous frame.
  return Object.freeze({
    contactFrame:profile.contactFrame,
    frameCount:profile.frameCount,
    hitAt:clip.duration*((profile.contactFrame-1)/profile.frameCount+1e-6),
    hitbox:Object.freeze({range:profile.range,halfWidth:profile.halfWidth}),
    confidence:profile.confidence,
    note:profile.note
  });
}
