// Reference logic from the browser prototype (JavaScript). Port the BEHAVIOUR to C#; do not copy syntax.
// Names with no match in the source are listed at the bottom.

// ===== wpnDmg =====
function wpnDmg() {
  const wt = wType(), w = D.eq.weapon, wd = w ? w.dmg : 0, all = 1 + TS.allP / 100;
  if (wt === 'bow') return (5 + D.dex * 1.7 + wd + D.str * TS.strToBow + D.mag * TS.magToBow) * (1 + TS.rangedP / 100) * all;
  if (wt === 'wand' || wt === 'staff') return (4 + D.mag * 1.5 + wd) * (1 + TS.spellP / 100) * (1 + eqStat('sp')) * all;
  return (5 + D.str * 1.8 + wd + D.mag * TS.magToDmg) * (1 + TS.meleeP / 100) * all;
}

// ===== gainXp =====
function gainXp(n) {
  D.xp += n; let up = 0;
  while (D.xp >= xpNeed()) { D.xp -= xpNeed(); D.level++; D.attrPts += 2; D.skillPts += 2; up++; }
  return up;
}

// ===== rollItem =====
function rollItem(level, bonus = 0, slot) {
  const x = Math.random();
  let idx = x < .55 ? 0 : x < .82 ? 1 : x < .94 ? 2 : x < .985 ? 3 : 4;
  if (Math.random() < bonus * .25 && idx < 4) idx++;
  level = Math.max(1, level);
  if (!slot) { const q = Math.random(); slot = q < .3 ? 'weapon' : q < .58 ? 'armor' : q < .75 ? 'shield' : 'trinket'; }
  const m = MULT[idx], it = { slot, rar: idx, lvl: level, dmg: 0, def: 0, hp: 0, mp: 0, name: '' };
  if (slot === 'weapon') {
    const q = Math.random(), wt = q < .34 ? 'sword' : q < .56 ? 'bow' : q < .78 ? 'wand' : 'staff'; it.wt = wt;
    const dm = { sword: 1, bow: .95, wand: .62, staff: .8 }[wt]; it.dmg = Math.floor((3 + level * 2.2) * m * dm);
    if (wt === 'wand') it.sp = Math.round((.06 + level * .006) * m * 100) / 100;
    if (wt === 'staff') { it.sp = Math.round((.12 + level * .01) * m * 100) / 100; it.two = true; it.mc = .1; }
    if (wt === 'bow') it.two = true;
  }
  else if (slot === 'armor') { it.def = Math.floor((1 + level * 1.1) * m); it.hp = Math.floor(level * 2 * m); }
  else if (slot === 'shield') { it.def = Math.floor((1 + level * .9) * m); it.hp = Math.floor(level * m); }
  else { it.hp = Math.floor((6 + level * 3) * m); it.mp = Math.floor((4 + level * 2) * m); it.def = idx >= 2 ? Math.floor(level * .5 * m) : 0; }
  it.name = IPRE[idx] + ' ' + (slot === 'weapon' ? WNAMES[it.wt][Math.min(4, Math.floor(Math.random() * 5))] : pick(INAMES[slot])) + (idx === 4 ? ' of the Rift' : '');
  return it;
}

// ===== describe =====
function describe(it) { const p = []; if (it.sp) p.push('+' + Math.round(it.sp * 100) + '% spell'); if (it.dmg) p.push('+' + it.dmg + ' dmg'); if (it.def) p.push('+' + it.def + ' def'); if (it.hp) p.push('+' + it.hp + ' hp'); if (it.mp) p.push('+' + it.mp + ' mp'); return `${it.name} (L${it.lvl}) ${p.join(', ')}`; }

// ===== recalcTree =====
function recalcTree() {
  const t = zeroTS(); const ok = new Set((D.tree || []).filter(i => TNODES[i]));
  for (const id of ok) for (const k in TNODES[id].mods) t[k] += TNODES[id].mods[k];
  for (const k in t) if (typeof t[k] === 'number') t[k] = Math.round(t[k] * 100) / 100;
  TS = t;
}

// ===== tCanAlloc =====
function tCanAlloc(n) { return n.id !== 0 && !tHas(n.id) && D.skillPts > 0 && n.adj.some(a => a === 0 || tHas(a)); }

// ===== tCanDealloc =====
function tCanDealloc(n) {
  if (n.id === 0 || !tHas(n.id)) return false;
  const rest = new Set(D.tree.filter(i => i !== n.id)), seen = new Set(), q = [0];
  while (q.length) { const c = q.pop(); for (const a of TNODES[c].adj) if (rest.has(a) && !seen.has(a)) { seen.add(a); q.push(a); } }
  return seen.size === rest.size;
}

// ===== startAttack =====
function startAttack(power) {
  if (P.atk || P.roll || P.stagger > 0 || P.cast || P.dead) return false;
  const wt = wType(), ranged = wt !== 'sword';
  const cost = ranged ? (power ? 16 : 7) : (power ? 26 : 9); if (P.st < 4) { note('Out of stamina', '#9ad'); return false; }
  useStamina(cost);
  let type = 3;
  if (ranged) type = wt === 'staff' ? 5 : 4; else if (!power) { P.comboN = P.comboT > 0 ? (P.comboN + 1) % 3 : 0; type = P.comboN; }
  const base = wt === 'sword' ? (power ? .85 : .5) : wt === 'bow' ? (power ? 1.0 : .6) : wt === 'wand' ? (power ? .7 : .4) : (power ? 1.1 : .85);
  P.atk = { t: 0, dur: base / atkSpeed(), type, power, hit: false, wt, comboHit: !ranged && !power && P.comboN === 2 };
  P.yaw = P.camYaw;
  if (!ranged) {
    let best = null, bd = 5.5;
    for (const e of EN()) { if (e.dead || !e.loaded) continue; const dx = e.x - P.x, dz = e.z - P.z, d = Math.hypot(dx, dz) - e.r; if (d < bd && Math.abs(angDiff(Math.atan2(dx, dz), P.camYaw)) < .9) { bd = d; best = e; } }
    if (best) P.yaw = Math.atan2(best.x - P.x, best.z - P.z);
  }
  Sfx.swing(); return true;
}

// ===== fireWeapon =====
function fireWeapon(at) {
  const wt = at.wt, pw = at.power, hx = P.x + Math.sin(P.yaw) * .6, hy = P.y + 1.45 + (P.mounted ? 1.2 : 0), hz = P.z + Math.cos(P.yaw) * .6;
  const t = aimPoint(80); let dx = t.x - hx, dy = t.y - hy, dz = t.z - hz; const l = Math.hypot(dx, dy, dz) || 1; dx /= l; dy /= l; dz /= l;
  const crit = Math.random() < critChance(); let dmg = wpnDmg() * R(.9, 1.1) * (crit ? critMult() : 1);
  if (wt === 'bow') {
    const n = TS.multi ? 3 : 1, sp = pw ? 64 : 50, pd = dmg * (pw ? 2.0 : 1) * (n > 1 ? .55 : 1);
    for (let k = 0; k < n; k++) { const a = (k - (n - 1) / 2) * .11, cx = Math.cos(a), sx = Math.sin(a); spawnProj({ kind: 'arrow', own: 'p', pl: true, x: hx, y: hy, z: hz, vx: (dx * cx - dz * sx) * sp, vy: dy * sp + .6, vz: (dx * sx + dz * cx) * sp, life: 2.4, dmg: pd, crit, pierce: pw || TS.pierce, hits: new Set() }); }
    Sfx.swing();
  } else if (wt === 'wand') {
    const cost = pw ? 8 : 3, ok = P.mp >= cost; P.mp = Math.max(0, P.mp - cost); if (!ok) dmg *= .5;
    spawnProj({ kind: 'bolt', own: 'p', pl: true, x: hx, y: hy, z: hz, vx: dx * 44, vy: dy * 44, vz: dz * 44, life: 1.6, dmg: dmg * (pw ? 1.9 : 1), crit, pierce: TS.pierce, hits: new Set() }); Sfx.zap();
  } else {
    const cost = pw ? 16 : 8, ok = P.mp >= cost; P.mp = Math.max(0, P.mp - cost); if (!ok) dmg *= .5;
    spawnProj({ kind: 'bolt', own: 'p', pl: true, big: true, x: hx, y: hy, z: hz, vx: dx * 28, vy: dy * 28, vz: dz * 28, life: 2.2, dmg: dmg * (pw ? 2.6 : 1.35), crit, aoe: pw ? 3.4 : 2.2 }); Sfx.fire();
  }
}

// ===== resolveAttackHit =====
function resolveAttackHit() {
  const at = P.atk; if (at.wt && at.wt !== 'sword') return fireWeapon(at);
  const cl = TS.cleave && (at.power || at.comboHit), range = (at.power ? 3.6 : 3.0) + (P.mounted ? .8 : 0) + (cl ? .8 : 0), arc = cl ? PI : at.power ? 1.35 : 1.0;
  let hit = 0;
  for (const e of EN()) {
    if (e.dead || !e.loaded) continue;
    const dx = e.x - P.x, dz = e.z - P.z, d = Math.hypot(dx, dz) - e.r;
    if (d > range || Math.abs(angDiff(Math.atan2(dx, dz), P.yaw)) > arc + (e.r > 1 ? .5 : 0)) continue;
    if (Math.abs((e.y + (e.m ? e.m.h * .4 : 1)) - (P.y + 1)) > 3.5 + e.r) continue;
    const crit = Math.random() < critChance();
    let dmg = wpnDmg() * R(.9, 1.1) * (at.power ? 2.2 : at.comboHit ? 1.35 : 1) * (crit ? critMult() : 1);
    hurtEnemy(e, dmg, { crit, knock: at.power ? 9 : at.comboHit ? 5 : 2.5, from: P, stun: at.power ? .6 : 0 }); hit++;
  }
  if (hit) { Sfx.hit(); CAM.shake = Math.max(CAM.shake, at.power ? .25 : .08); }
}

// ===== castSpell =====
function castSpell(i) {
  const sp = SPELLS[i];
  if (P.dead || P.cast || P.roll || P.stagger > 0 || P.mounted) { if (P.mounted) note('You cannot cast while mounted', '#9ad'); return; }
  if (P.cd[i] > 0) return;
  const mc = Math.round(sp.mp * (1 + TS.mcost / 100) * (1 - ((D.eq.weapon && D.eq.weapon.mc) || 0)));
  if (P.mp < mc) { note('Not enough mana', '#6aa8ff'); return; }
  P.mp -= mc; P.cd[i] = sp.cd * (1 - TS.cdr / 100); P.cast = { i, t: 0, dur: sp.cast }; P.yaw = P.camYaw; P.atk = null;
}

// ===== fireSpell =====
function fireSpell(i) {
  const sp = SPELLS[i], pow = spellPow(), hx = P.x + Math.sin(P.yaw) * .6, hy = P.y + 1.4, hz = P.z + Math.cos(P.yaw) * .6;
  if (sp.id === 'fire') {
    const t = aimPoint(70); let dx = t.x - hx, dy = t.y - hy, dz = t.z - hz; const l = Math.hypot(dx, dy, dz) || 1;
    spawnProj({ kind: 'fire', own: 'p', x: hx, y: hy, z: hz, vx: dx / l * 38, vy: dy / l * 38, vz: dz / l * 38, life: 2.2, dmg: (20 + D.level * 3.2) * pow, aoe: 3 });
    Sfx.fire();
  } else if (sp.id === 'nova') {
    const dmg = (14 + D.level * 2.6) * pow; Sfx.ice();
    for (let a = 0; a < 70; a++) { const an = a / 70 * TAU, sp2 = R(6, 11); emit(P.x, P.y + .8, P.z, Math.cos(an) * sp2, R(0, 1.5), Math.sin(an) * sp2, R(.5, .9), R(.3, .7), .6, .85, 1, 0, 0); }
    addRing(P.x, P.y + .1, P.z, 7.5, 0x8ad4ff);
    for (const e of EN()) { if (e.dead || !e.loaded) continue; if (Math.hypot(e.x - P.x, e.z - P.z) - e.r < 7.5) { e.slow = 4; hurtEnemy(e, dmg, { knock: 4, from: P, ice: true, stun: .3 }); } }
  } else if (sp.id === 'heal') {
    const amt = maxHp() * .35 * (1 + D.skills.resto * .06); P.hp = Math.min(maxHp(), P.hp + amt); Sfx.heal();
    for (let k = 0; k < 40; k++) emit(P.x + R(-.6, .6), P.y + R(0, 1.8), P.z + R(-.6, .6), R(-.3, .3), R(1, 2.4), R(-.3, .3), R(.7, 1.2), R(.25, .5), .5, 1, .6, 0, 0);
    floatText(P.x, P.y + 2.2, '+' + Math.round(amt), '#7dff9a');
  } else if (sp.id === 'zap') {
    const t = aimPoint(40); let cur = t.e || null;
    if (!cur) { let bd = 28; for (const e of EN()) { if (e.dead || !e.loaded) continue; const d = Math.hypot(e.x - P.x, e.z - P.z); if (d < bd && Math.abs(angDiff(Math.atan2(e.x - P.x, e.z - P.z), P.camYaw)) < 1.2) { bd = d; cur = e; } } }
    Sfx.zap();
    if (!cur) { zapLine(hx, hy, hz, t.x, t.y, t.z); return; }
    let px = hx, py = hy, pz = hz, dmg = (24 + D.level * 3) * pow; const hitSet = new Set();
    for (let j = 0; j < 4 + TS.chain && cur; j++) {
      const cy = cur.y + (cur.m ? cur.m.h * .55 : 1.1); zapLine(px, py, pz, cur.x, cy, cur.z); hitSet.add(cur);
      hurtEnemy(cur, dmg, { from: P, knock: 1.5, stun: .25, zap: true }); dmg *= .82; px = cur.x; py = cy; pz = cur.z;
      let nx = null, bd = 10; for (const e of EN()) { if (e.dead || !e.loaded || hitSet.has(e)) continue; const d = Math.hypot(e.x - px, e.z - pz); if (d < bd) { bd = d; nx = e; } }
      cur = nx;
    }
  }
}

// ===== updateProj =====
function updateProj(dt) {
  for (let i = PROJ.length - 1; i >= 0; i--) {
    const p = PROJ[i]; p.life -= dt;
    if (p.kind === 'arrow') p.vy -= 7 * dt;
    p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
    p.mesh.position.set(p.x, p.y, p.z); if (p.kind === 'arrow') p.mesh.lookAt(p.x + p.vx, p.y + p.vy, p.z + p.vz);
    if (p.kind === 'fire') emit(p.x, p.y, p.z, R(-.5, .5), R(-.2, .8), R(-.5, .5), .35, p.big ? 1.1 : .55, 1, .5 + Math.random() * .3, .1, 0, 0);
    else if (p.kind === 'bolt') emit(p.x, p.y, p.z, R(-.4, .4), R(-.4, .4), R(-.4, .4), .4, .5, .7, .4, 1, 0, 0);
    let end = p.life <= 0, hitE = null;
    if (!end && p.y < Z.ground(p.x, p.z) + .05) end = true;
    if (!end && Z.kind === 'dun' && Z.solid(p.x, p.z)) end = true;
    if (!end && p.own === 'p') { for (const e of EN()) { if (e.dead || !e.loaded || (p.hits && p.hits.has(e))) continue; if (Math.hypot(e.x - p.x, e.z - p.z) < e.r + .5 && p.y > e.y - .3 && p.y < e.y + (e.m ? e.m.h : 2) + .5) {
        if (p.pierce && !p.aoe) { hurtEnemy(e, p.dmg, { crit: p.crit, pl: true, from: { x: p.x - p.vx, z: p.z - p.vz }, knock: 1.5 }); p.hits.add(e); continue; }
        hitE = e; end = true; break; } } }
    if (!end && p.own === 'e' && !P.dead && Math.hypot(P.x - p.x, P.z - p.z) < .75 && p.y > P.y - .2 && p.y < P.y + 2.1) { damagePlayer(p.dmg, { x: p.x - p.vx, z: p.z - p.vz }, { proj: true }); end = true; burst(p.x, p.y, p.z, 8, 4, .4, .4, 1, .5, .2, 0); }
    if (end) {
      if (p.own === 'p') {
        if (p.aoe) {
          burst(p.x, p.y, p.z, 30, 7, .6, .9, 1, .55, .15, -1); burst(p.x, p.y, p.z, 12, 4, .8, 1.2, 1, .9, .4, -2); Sfx.hit(); CAM.shake = Math.max(CAM.shake, .12);
          for (const e of EN()) { if (e.dead || !e.loaded) continue; const d = Math.hypot(e.x - p.x, e.z - p.z) - e.r; if (d < p.aoe) { hurtEnemy(e, p.dmg * (1 - clamp(d / p.aoe, 0, 1) * .4), { from: { x: p.x, z: p.z }, knock: 4, stun: .25, fire: true }); e.burn = 3; e.burnDps = p.dmg * .12 * (1 + TS.burn / 100); } }
        } else if (hitE) hurtEnemy(hitE, p.dmg, { crit: p.crit, pl: true, from: { x: p.x - p.vx, z: p.z - p.vz }, knock: 2.2 });
      } else if (p.aoe && p.kind === 'fire') {
        burst(p.x, p.y, p.z, 24, 6, .6, 1, 1, .4, .1, -1);
        if (!P.dead && Math.hypot(P.x - p.x, P.z - p.z) < p.aoe) damagePlayer(p.dmg, { x: p.x, z: p.z }, { aoe: true });
      }
      entRoot.remove(p.mesh); PROJ.splice(i, 1);
    }
  }
}

// ===== hurtEnemy =====
function hurtEnemy(e, dmg, o) {
  if (e.dead) return;
  o = o || {};
  if (e.blocking && !o.dot && !o.fire) { dmg *= .3; Sfx.block(); burst(e.x, e.y + 1.2, e.z, 6, 4, .3, .3, 1, .9, .6, 0); }
  dmg = Math.max(1, Math.round(dmg)); e.hp -= dmg; e.flash = .12;
  if ((o.from === P || o.pl) && !o.dot) { if (TS.leech) P.hp = Math.min(maxHp(), P.hp + dmg * TS.leech / 100); if (TS.mleech) P.mp = Math.min(maxMp(), P.mp + dmg * TS.mleech / 100); }
  if (e.state === 'sleep') wakeDragon(e); else if (e.state === 'idle') aggroEnemy(e);
  floatAt(e, o.crit ? dmg + '!' : '' + dmg, o.crit ? '#ffd24a' : o.ice ? '#9ae0ff' : o.fire ? '#ffb060' : '#fff');
  if (o.from && o.knock && !e.def.boss) { const dx = e.x - o.from.x, dz = e.z - o.from.z, l = Math.hypot(dx, dz) || 1; e.kx += dx / l * o.knock * 1.6; e.kz += dz / l * o.knock * 1.6; }
  if (o.stun && !(e.def.boss && o.stun < .5) && e.def.kind !== 'dragon') { e.stun = Math.max(e.stun, o.stun * (e.def.boss ? .5 : 1)); if (e.state === 'wind') { e.state = 'stagger'; e.atkP = -1; } }
  if (!o.dot) { burst(e.x, e.y + 1.1, e.z, o.crit ? 14 : 7, 4, .35, .35, 1, .3, .2, 4); e.lastHit = GAME.t; setTarget(e); }
  if (e.hp <= 0) killEnemy(e);
}

// ===== damagePlayer =====
function damagePlayer(raw, src, o) {
  o = o || {};
  if (P.dead || P.inv > 0 || P.shiftT > 0) return false;
  const base = raw * 100 / (100 + defense() * 5);
  let dmg = base;
  const front = src ? Math.abs(angDiff(Math.atan2(src.x - P.x, src.z - P.z), P.yaw)) < 1.25 : true;
  if (P.blocking && front && !o.aoeNoBlock) {
    if (P.blockT < .22 && src && src.def) { Sfx.block(); burst(P.x + Math.sin(P.yaw) * .7, P.y + 1.3, P.z + Math.cos(P.yaw) * .7, 14, 5, .35, .4, 1, .95, .6, 0); src.stun = Math.max(src.stun, .9); src.state = 'stagger'; src.atkP = -1; floatText(P.x, P.y + 2.2, 'PARRY', '#ffe08a', P.z); return false; }
    const sh = D.eq.shield, red = clamp(.55 + (sh ? sh.def * .012 : 0) + TS.block / 100, .5, .92);
    dmg = base * (1 - red); useStamina(raw * .3);
    Sfx.block(); burst(P.x + Math.sin(P.yaw) * .7, P.y + 1.3, P.z + Math.cos(P.yaw) * .7, 8, 4, .3, .35, 1, .9, .6, 0);
    if (P.st <= 0) { P.blocking = false; P.stagger = .7; floatText(P.x, P.y + 2.2, 'Guard broken', '#ff9a7a', P.z); }
  } else { Sfx.hurt(); if (o.knock && src) { const dx = P.x - src.x, dz = P.z - src.z, l = Math.hypot(dx, dz) || 1; P.vx += dx / l * o.knock; P.vz += dz / l * o.knock; } if (!o.proj && !o.dot) P.stagger = Math.max(P.stagger, .18); }
  if (TS.manaShield && P.mp > 0) { const m = Math.min(P.mp, dmg * .35); P.mp -= m; dmg -= m; }
  if (TS.thorns && src && src.def && !o.proj && !o.aoe && Math.hypot(src.x - P.x, src.z - P.z) < 6) hurtEnemy(src, raw * TS.thorns / 100, { from: P, thorns: true });
  dmg = Math.max(1, Math.round(dmg)); P.hp -= dmg; P.hurt = .45; P.inv = .2; P.lastHurt = GAME.t; CAM.shake = Math.max(CAM.shake, .18);
  floatText(P.x, P.y + 2, '-' + dmg, '#ff6b5f', P.z);
  $('vig').style.background = 'radial-gradient(ellipse at center, transparent 40%, rgba(160,10,10,.55) 100%)'; setTimeout(() => { $('vig').style.background = ''; }, 220);
  if (P.hp <= 0) { if (TS.undying && GAME.t > (P.undyReady || 0)) { P.hp = Math.max(1, maxHp() * .3); P.undyReady = GAME.t + 180; P.inv = 2; note('Undying: you refuse to fall', '#ffd24a'); burst(P.x, P.y + 1, P.z, 30, 6, .8, .6, 1, .85, .3, 0); } else playerDie(); }
  return true;
}

// ===== makeEnemy =====
function makeEnemy(type, x, z, lvl, o) {
  const d = ET[type], lv = Math.max(1, lvl || 1);
  const e = Object.assign({
    id: ++EID, type, def: d, name: d.n, lvl: lv, x, z, y: Z ? Z.ground(x, z) : groundAt(x, z), yaw: Math.random() * TAU, hp: lvlHp(d, lv), max: lvlHp(d, lv), dmg: lvlDmg(d, lv), r: d.r, home: { x, z }, realm: d.realm,
    group: 0, boss: !!d.boss, state: d.kind === 'dragon' ? 'sleep' : 'idle', cd: R(0, 1), tw: 0, stun: 0, slow: 0, burn: 0, burnDps: 0, flash: 0, walk: R(0, 6), moveSpd: 0, curSpeed: 0, atkP: -1, atkType: 0, atkT: 0, t: R(0, 6),
    wander: { x, z, t: R(1, 4) }, dead: false, deadT: 0, loaded: false, mesh: null, m: null, kx: 0, kz: 0, strafe: Math.random() < .5 ? 1 : -1, respawn: 0, blocking: false, summonT: R(6, 12), special: 0, dragonT: 0
  }, o || {});
  return e;
}

// ===== updateEnemy =====
function updateEnemy(e, dt) {
  const d = e.def;
  e.cd -= dt; e.stun -= dt; e.slow -= dt; e.flash -= dt; e.t += dt; e.curSpeed = 0;
  if (e.burn > 0) { e.burn -= dt; e.burnAcc = (e.burnAcc || 0) + dt; if (Math.random() < .5) emit(e.x + R(-.3, .3), e.y + R(.3, 1.6), e.z + R(-.3, .3), 0, R(.8, 1.8), 0, .4, .5, 1, .5, .1, 0, 0); if (e.burnAcc >= .5) { e.burnAcc = 0; hurtEnemy(e, e.burnDps * .5, { dot: true, fire: true }); if (e.dead) return; } }
  if (e.kx || e.kz) { moveEnt(e, e.kx * dt, e.kz * dt, e.r); const f = Math.pow(.02, dt); e.kx *= f; e.kz *= f; if (Math.abs(e.kx) < .05) e.kx = 0; if (Math.abs(e.kz) < .05) e.kz = 0; }
  e.y = Z.ground(e.x, e.z);
  const pdx = P.x - e.x, pdz = P.z - e.z, dist = Math.hypot(pdx, pdz), ok = !P.dead;
  if (e.state === 'sleep') { if (ok && dist < 24) wakeDragon(e); return; }
  if (e.roar > 0) { e.roar -= dt; faceTo(e, P.x, P.z, dt, 4); if (e.roar < 1.6 && !e.roared) { e.roared = true; if (dist < 22 && ok) { P.vx += (P.x - e.x) / (dist || 1) * 9; P.vz += (P.z - e.z) / (dist || 1) * 9; } } return; }
  if (e.stun > 0) { e.state = 'stagger'; return; } else if (e.state === 'stagger') e.state = 'chase';
  const slow = e.slow > 0 ? .5 : 1;
  if (e.def.boss && !e.lastBoss && e.state === 'idle' && ok && dist < d.aggro) { aggroEnemy(e); }
  if (e.state === 'idle') {
    if (ok && dist < d.aggro * (P.sprint ? 1.25 : 1) * (e.type === 'clone' ? 9 : 1)) { aggroEnemy(e); return; }
    e.wander.t -= dt;
    if (e.wander.t <= 0) { e.wander.t = R(3, 7); const a = R(0, TAU), rr = R(2, 7); e.wander.x = e.home.x + Math.cos(a) * rr; e.wander.z = e.home.z + Math.sin(a) * rr; e.wander.go = Math.random() < .6; }
    if (e.wander.go && Math.hypot(e.wander.x - e.x, e.wander.z - e.z) > 1) steer(e, e.wander.x, e.wander.z, d.spd * .35 * slow, dt, 3);
    return;
  }
  if (e.state === 'return') {
    steer(e, e.home.x, e.home.z, d.spd * 1.1, dt, 6); e.hp = Math.min(e.max, e.hp + e.max * .2 * dt);
    if (Math.hypot(e.home.x - e.x, e.home.z - e.z) < 1.5) e.state = 'idle'; if (ok && dist < d.aggro * .6) e.state = 'chase'; return;
  }
  if (!ok) { e.state = 'return'; return; }
  const homeD = Math.hypot(e.x - e.home.x, e.z - e.home.z);
  if (e.state === 'chase' && !e.arena && !e.noLeash && (homeD > 75 || dist > d.aggro * 2.6)) { e.state = 'return'; return; }
  if (d.boss && e.hp < e.max * .7 && d.summon && Z.kind !== 'over') { e.summonT -= dt; if (e.summonT <= 0 && countMinions(e) < 4) { e.summonT = 16; summonMinions(e); } }
  if (e.type === 'clone') { cloneAI(e, dt, dist); return; }
  if (d.kind === 'dragon') { dragonAI(e, dt, dist); return; }
  if (e.state === 'wind') {
    e.tw -= dt; faceTo(e, P.x, P.z, dt, 5); e.atkP = clamp((1 - e.tw / d.wind) * .5, 0, .5);
    if (e.tw <= 0) { enemyStrike(e); e.state = 'recover'; e.tw = .5; }
    return;
  }
  if (e.state === 'recover') { e.tw -= dt; e.atkP = .5 + clamp(1 - e.tw / .5, 0, 1) * .5; if (e.tw <= 0) { e.state = 'chase'; e.atkP = -1; } return; }
  // chase
  const reach = d.ranged ? d.range : d.reach + e.r * .6;
  if (d.ranged) {
    faceTo(e, P.x, P.z, dt, 7);
    const keep = d.keep;
    if (dist > d.range * .92) steer(e, P.x, P.z, d.spd * slow, dt, 7);
    else if (dist < keep - 3) { const a = Math.atan2(pdx, pdz) + PI; steer(e, e.x + Math.sin(a) * 5, e.z + Math.cos(a) * 5, d.spd * .9 * slow, dt, 7); faceTo(e, P.x, P.z, dt, 7); }
    else { const a = Math.atan2(pdx, pdz) + e.strafe * PI / 2; steer(e, e.x + Math.sin(a) * 4, e.z + Math.cos(a) * 4, d.spd * .5 * slow, dt, 7); faceTo(e, P.x, P.z, dt, 7); if (Math.random() < dt * .3) e.strafe *= -1; }
    if (e.cd <= 0 && dist < d.range && dist > 3) { e.state = 'wind'; e.tw = d.wind; e.cd = d.cd; e.atkType = 0; }
  } else {
    if (dist > reach) steer(e, P.x, P.z, d.spd * slow * (dist > 14 ? 1.15 : 1), dt, 8); else faceTo(e, P.x, P.z, dt, 8);
    if (dist <= reach + .5 && e.cd <= 0) { e.state = 'wind'; e.tw = d.wind; e.cd = d.cd * R(.9, 1.2); e.atkType = RI(0, 2); e.atkT = 0; }
  }
}

// ===== genDungeon =====
function genDungeon(key, depth) {
  const th = DTH[key], pp = poi(key), rnd = mulberry32((Date.now() & 0xffffff) + depth * 7919);
  const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
  const grid = new Uint8Array(GW * GH), rooms = [];
  const carve = (x, z) => { if (x > 0 && z > 0 && x < GW - 1 && z < GH - 1) grid[z * GW + x] = 1; };
  for (let t = 0; t < 400 && rooms.length < 13; t++) {
    const w = ri(5, 9), h = ri(5, 9), x = ri(2, GW - w - 3), z = ri(2, GH - h - 3);
    if (rooms.some(r => x < r.x + r.w + 2 && x + w + 2 > r.x && z < r.z + r.h + 2 && z + h + 2 > r.z)) continue;
    rooms.push({ x, z, w, h, cx: x + (w >> 1), cz: z + (h >> 1) }); for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) carve(x + i, z + j);
  }
  for (let i = 1; i < rooms.length; i++) {
    const a = rooms[i - 1], b = rooms[i]; let x = a.cx, z = a.cz;
    while (x !== b.cx) { carve(x, z); x += Math.sign(b.cx - x); } while (z !== b.cz) { carve(x, z); z += Math.sign(b.cz - z); } carve(x, z);
  }
  let bi = 0, bd = -1; rooms.forEach((r, i) => { const d = Math.hypot(r.cx - rooms[0].cx, r.cz - rooms[0].cz); if (d > bd) { bd = d; bi = i; } });
  // meshes
  const floors = [], walls = [];
  for (let z = 0; z < GH; z++) for (let x = 0; x < GW; x++) {
    if (grid[z * GW + x]) { floors.push([x, z]); continue; }
    let adj = false; for (let dz = -1; dz <= 1 && !adj; dz++) for (let dx = -1; dx <= 1; dx++) { const nx = x + dx, nz = z + dz; if (nx >= 0 && nz >= 0 && nx < GW && nz < GH && grid[nz * GW + nx]) { adj = true; break; } }
    if (adj) walls.push([x, z]);
  }
  const mkI = (geo, hex, n, cast) => instMesh(geo, new THREE.MeshLambertMaterial({ color: 0xffffff }), n, cast);
  const fl = mkI(GEO.box, 0, floors.length, false), ce = mkI(GEO.box, 0, floors.length, false), wl = mkI(GEO.box, 0, walls.length, false);
  const col = new THREE.Color(), cF = new THREE.Color(th.floor), cW = new THREE.Color(th.wall);
  floors.forEach(([x, z], i) => { setInst(fl, i, (x + .5) * TILE, -.3, (z + .5) * TILE, TILE, .6, TILE, 0); setInst(ce, i, (x + .5) * TILE, WALLH + .3, (z + .5) * TILE, TILE, .6, TILE, 0); const v = .85 + rnd() * .3; fl.setColorAt(i, col.copy(cF).multiplyScalar(v)); ce.setColorAt(i, col.copy(cW).multiplyScalar(.55)); });
  walls.forEach(([x, z], i) => { const h = WALLH + .6; setInst(wl, i, (x + .5) * TILE, h / 2 - .3, (z + .5) * TILE, TILE, h, TILE, 0); wl.setColorAt(i, col.copy(cW).multiplyScalar(.8 + rnd() * .35)); });
  patchWorld(fl.material, TEX.flag, 3.5, 1.5); patchWorld(ce.material, TEX.rock, 4, 1.2); patchWorld(wl.material, TEX.brick, 2.4, 1.45);
  [fl, ce, wl].forEach(m => { m.instanceMatrix.needsUpdate = true; if (m.instanceColor) m.instanceColor.needsUpdate = true; dun.add(m); });
  fl.receiveShadow = false;
  const solid = (x, z) => { const tx = Math.floor(x / TILE), tz = Math.floor(z / TILE); return tx < 0 || tz < 0 || tx >= GW || tz >= GH || grid[tz * GW + tx] === 0; };
  const Zd = {
    kind: 'dun', key, depth, th, grid, ground: () => 0, enemies: [], loot: [], chests: [], lights: [], pads: [], bossDead: false, solid, rooms, name: th.name, group: dun,
    blocked: (nx, nz) => solid(nx, nz),
    push: (e, r) => {
      for (let it = 0; it < 2; it++) {
        const x0 = Math.floor((e.x - r) / TILE), x1 = Math.floor((e.x + r) / TILE), z0 = Math.floor((e.z - r) / TILE), z1 = Math.floor((e.z + r) / TILE);
        for (let tz = z0; tz <= z1; tz++) for (let tx = x0; tx <= x1; tx++) {
          if (!(tx < 0 || tz < 0 || tx >= GW || tz >= GH || grid[tz * GW + tx] === 0)) continue;
          const bx0 = tx * TILE, bz0 = tz * TILE, cx = clamp(e.x, bx0, bx0 + TILE), cz = clamp(e.z, bz0, bz0 + TILE), dx = e.x - cx, dz = e.z - cz, d = Math.hypot(dx, dz);
          if (d < r) { if (d > 1e-5) { e.x += dx / d * (r - d); e.z += dz / d * (r - d); } else { e.x = bx0 + TILE / 2 + (e.x < bx0 + TILE / 2 ? -TILE / 2 - r : TILE / 2 + r); } }
        }
      }
    },
    camBlocked: (x, y, z) => y < .25 || y > WALLH - .25 || solid(x, z), list: () => Zd.enemies
  };
  // braziers / lights
  rooms.forEach((r, ri_) => {
    const cs = [[r.x + .6, r.z + .6], [r.x + r.w - .6, r.z + .6], [r.x + .6, r.z + r.h - .6], [r.x + r.w - .6, r.z + r.h - .6]];
    cs.forEach((c, k) => {
      if ((k + ri_) % 2) return; const x = c[0] * TILE, z = c[1] * TILE, g = new THREE.Group(); g.position.set(x, 0, z);
      g.add(M(GEO.cylL, MAT.stone, .5, 1.1, .5, 0, .55, 0), M(GEO.cyl, MAT.dark, .6, .2, .6, 0, 1.15, 0)); flameAt(g, 0, 1.55, 0, 2, th.flame); dun.add(g); addCircle2(Zd, x, z);
      Zd.lights.push({ x, y: 2, z, l: new THREE.Color(th.torch), d: new THREE.Color(th.torch), i: 1.4, dist: 24, realm: -1, ph: rnd() * 6 });
    });
  });
  // decor: pillars, rubble, banners
  rooms.forEach((r, ri_) => {
    const rr2 = mulberry32(ri_ * 77 + depth * 13 + 5);
    if (r.w >= 6 && r.h >= 6) [[.28, .28], [.72, .28], [.28, .72], [.72, .72]].forEach(c => { if (rr2() < .45) return; const px = (r.x + r.w * c[0]) * TILE, pz = (r.z + r.h * c[1]) * TILE, pg = new THREE.Group(); pg.position.set(px, 0, pz);
      pg.add(M(GEO.box, MAT.stone, 1.5, .5, 1.5, 0, .25, 0), M(GEO.cyl, MAT.stone, .55, WALLH - 1, .55, 0, WALLH / 2, 0), M(GEO.box, MAT.stone, 1.3, .4, 1.3, 0, WALLH - .2, 0)); dun.add(pg); Zd.pillars = (Zd.pillars || []); Zd.pillars.push({ x: px, z: pz }); });
    for (let k = 0; k < 4; k++) { const rx = (r.x + .8 + rr2() * (r.w - 1.6)) * TILE, rz = (r.z + .8 + rr2() * (r.h - 1.6)) * TILE, sz = .3 + rr2() * .5; const m = M(GEO.dodec, MAT.rock, sz * 1.3, sz, sz * 1.2, rx, sz * .4, rz); m.rotation.set(rr2() * 3, rr2() * 6, rr2() * 3); dun.add(m); }
    if (rr2() < .8) { const bx = (r.x + r.w * (.3 + rr2() * .4)) * TILE, bz = (r.z - .02) * TILE + .02; const bn = M(GEO.box, MAT.crim, 1.2, 3, .08, bx, WALLH - 2.2, bz + .05); const bn2 = M(GEO.box, MAT.gold, 1.4, .12, .12, bx, WALLH - .65, bz + .06); dun.add(bn, bn2); }
  });
  // entry / exit pad
  const er = rooms[0], ex = (er.cx + .5) * TILE, ez = (er.cz + .5) * TILE;
  makePad(Zd, ex, ez, 'exit', 0x7fe0ff);
  Zd.start = { x: ex, z: ez + 3 };
  // enemies, chests
  const baseLvl = pp.lvl + depth - 1;
  rooms.forEach((r, i) => {
    if (i === 0) return; const cx = (r.cx + .5) * TILE, cz = (r.cz + .5) * TILE, gid = ++GID;
    if (i === bi) {
      const boss = makeEnemy(th.boss, cx, cz, baseLvl + 1, { group: gid, noLeash: true }); Zd.enemies.push(boss); Zd.boss = boss;
      if (th.boss !== 'golem') for (let k = 0; k < 2; k++) { const t = pick(th.mobs.filter(m => m !== 'brute')); Zd.enemies.push(makeEnemy(t, cx + (k ? 6 : -6), cz + 4, baseLvl, { group: gid, noLeash: true })); }
      return;
    }
    const n = ri(2, 4) + (depth > 2 ? 1 : 0);
    for (let k = 0; k < n; k++) { const t = th.mobs[ri(0, th.mobs.length - 1)], a = k / n * TAU, rr = ri(2, 5) * 1.1; const x = cx + Math.cos(a) * rr, z = cz + Math.sin(a) * rr; if (solid(x, z)) continue; Zd.enemies.push(makeEnemy(t, x, z, baseLvl + (rnd() < .3 ? 1 : 0), { group: gid, noLeash: true })); }
    if (rnd() < .55) dunChest(Zd, (r.x + r.w - 1.2) * TILE, (r.z + r.h - 1.2) * TILE, depth > 1 ? 2 : 1, baseLvl);
  });
  Zd.bossRoom = rooms[bi]; Zd.baseLvl = baseLvl;
  return Zd;
}

// ===== startArena =====
function startArena(mode) {
  closePanel();
  if (S.realm) forceRealm(0);
  P.mounted = false; A.active = true; A.mode = mode; A.wave = 1; A.alive = 0; A.phase = 'prep'; A.delay = 3; A.streak = 1;
  setGate(false); placePlayer(ARENA.x - 12, ARENA.z); P.camYaw = P.yaw = -PI / 2 * 0 + PI / 2 * 1; P.hp = maxHp(); P.mp = maxMp(); P.st = maxSt();
  banner(mode === 'waves' ? 'Wave Survival' : 'Shadow Duel', 'the crowd roars'); Sfx.roar(); screenFlash('#fff');
  if (mode === 'duel') makeCloneDef();
}

// ===== arenaTick =====
function arenaTick(dt) {
  if (!A.active) return;
  const L = OWE[0]; for (let i = L.length - 1; i >= 0; i--) { const e = L[i]; if (e.arena && e.dead && !e.loaded) L.splice(i, 1); }
  if (P.dead) return;
  if (A.phase === 'prep') {
    A.delay -= dt; if (A.delay > 0) return;
    if (A.mode === 'waves') {
      const w = A.wave, n = Math.min(10, 2 + w), pool = w < 3 ? ['wolf', 'goblin', 'bandit'] : w < 5 ? ['goblin', 'bandit', 'archer', 'skeleton', 'wolf'] : w < 8 ? ['bandit', 'archer', 'skeleton', 'wraith', 'hound', 'goblin'] : ['skeleton', 'wraith', 'hound', 'archer', 'bandit', 'revenant'];
      const lvl = Math.max(1, D.level + Math.floor(w / 3) - 1), list = []; for (let i = 0; i < n; i++) list.push(pick(pool));
      if (w % 5 === 0) list.push(w >= 10 ? 'revenant' : 'brute');
      list.forEach((t, i) => spawnArena(t, lvl, i, list.length));
      banner('Wave ' + w, list.length + ' foes'); Sfx.roar();
    } else {
      makeCloneDef(); const e = spawnArena('clone', 1, 0, 1); if (e) { e.hp = e.max = ET.clone.hp; } banner('Round ' + A.streak, 'your shadow rises'); Sfx.roar();
    }
    A.phase = 'fight';
  } else if (A.alive <= 0) {
    A.phase = 'prep'; A.delay = 6;
    if (A.mode === 'waves') {
      const g = 14 * A.wave; D.gold += g; D.bestWave = Math.max(D.bestWave, A.wave); questEvent('wave', { wave: A.wave }); banner('Wave ' + A.wave + ' cleared', '+' + g + ' gold'); A.wave++;
    } else {
      const g = 45 * A.streak; D.gold += g; D.bestDuel = Math.max(D.bestDuel, A.streak); questEvent('duel', { streak: A.streak }); banner('Victory!', '+' + g + ' gold. Streak ' + A.streak); dropLoot(ARENA.x, ARENA.z, D.level + 1, .6, 2, 0, .3); A.streak++;
    }
    P.hp = Math.min(maxHp(), P.hp + maxHp() * .35); P.mp = maxMp(); crowd.cheer = 1.5; Sfx.level(); save();
  }
}

// ===== spawnArena =====
function spawnArena(type, lvl, i, n) {
  const a = i / n * TAU + WR(0, 1), rr = type === 'clone' ? 10 : 15;
  const x = ARENA.x + Math.cos(a) * rr, z = ARENA.z + Math.sin(a) * rr;
  const e = makeEnemy(type, x, z, lvl, { arena: true, state: 'chase', noLeash: true, realm: 0 }); OWE[0].push(e); loadEnemy(e); A.alive++; burst(x, e.y + 1, z, 14, 4, .6, .6, 1, .5, .3, 0); return e;
}

// ===== endArena =====
function endArena(died) {
  if (!A.active) return;
  for (const e of OWE[0]) if (e.arena) unloadEnemy(e);
  OWE[0] = OWE[0].filter(e => !e.arena);
  for (const p of PROJ.splice(0)) entRoot.remove(p.mesh);
  A.active = false; A.alive = 0; setGate(true);
  if (!died) { placePlayer(ARENA.x - 40, ARENA.z); P.camYaw = P.yaw = PI / 2; }
  save();
}

// ===== enterDungeon =====
function enterDungeon(key, depth) {
  depth = depth || 1; closePanel();
  const p = poi(key);
  if (Z.kind === 'over') {
    forceRealm(0); if (P.mounted) { P.mounted = false; }
    for (const L of OWE) for (const e of L) unloadEnemy(e);
    ZO.loot.forEach(l => { l.mesh.visible = false; });
    ZO.ret = { x: p.door.x, z: p.door.z }; flameStart = flames.length;
  } else teardownDungeon();
  const zd = genDungeon(key, depth); const ret = ZO.ret; Z = zd; zd.ret = ret;
  over.visible = false; dun.visible = true;
  placePlayer(zd.start.x, zd.start.z); P.camYaw = P.yaw = 0; P.target = null;
  banner(zd.th.name, 'depth ' + depth); screenFlash('#000'); D.deepest = Math.max(D.deepest, depth - 1);
  save();
}

// ===== leaveDungeon =====
function leaveDungeon(died) {
  if (Z.kind !== 'dun') return;
  teardownDungeon(); Z = ZO; over.visible = true; dun.visible = false;
  ZO.loot.forEach(l => { l.mesh.visible = true; });
  const r = ZO.ret || { x: 0, z: 9 }; placePlayer(r.x, r.z); P.camYaw = P.yaw = Math.atan2(r.x - 0, r.z - 0) || 0;
  if (!died) { screenFlash('#fff'); note('You emerge into daylight', '#cba'); }
  save();
}

// ===== onDungeonBossDown =====
function onDungeonBossDown(e) {
  if (Z.kind !== 'dun' || Z.boss !== e) return; Z.bossDead = true;
  D.deepest = Math.max(D.deepest, Z.depth);
  const b = Z.bossRoom, bx = (b.cx + .5) * TILE, bz = (b.cz + .5) * TILE;
  const pd = makePad(Z, bx, bz + 4, 'down', 0xffc060); const last = Z.pads[Z.pads.length - 1]; last.on = true; last.g.visible = true;
  dunChest(Z, bx - 5, bz - 4, 2, Z.baseLvl + 1); dunChest(Z, bx + 5, bz - 4, 2, Z.baseLvl + 1);
  note('A way down has opened. Descend for better loot and tougher foes.', '#ffc060');
}

// ===== shiftRealm =====
function shiftRealm() {
  if (Z.kind !== 'over' || A.active) { note('The realms are sealed here', '#b9a'); return; }
  if (S.shiftCd > 0) { note('The rift is still settling...', '#b9a'); return; }
  if (P.dead || P.roll) return;
  for (const e of OWE[S.realm]) unloadEnemy(e);
  S.realm ^= 1; S.shiftCd = 5; P.target = null; P.mounted = false;
  Sfx.realmShift(); Sfx.realm(S.realm); screenFlash(S.realm ? '#8a5aff' : '#fff'); CAM.shake = .3;
  for (let k = 0; k < 60; k++) emit(P.x + R(-1, 1), P.y + R(0, 2), P.z + R(-1, 1), R(-3, 3), R(0, 4), R(-3, 3), R(.5, 1), R(.3, .6), S.realm ? .7 : 1, S.realm ? .4 : .9, 1, 0, 0);
  banner(S.realm ? 'The Shadow' : 'The Light', S.realm ? 'you cross the rift' : 'you return');
  D.realm = S.realm;
}

// ===== forceRealm =====
function forceRealm(r) {
  if (S.realm === r) return;
  for (const e of OWE[S.realm]) unloadEnemy(e);
  S.realm = r; S.realmT = r; S.shiftCd = 0; setRealmVisual(r); Sfx.realm(r); P.target = null;
}

// ===== spawnOverworldEnemies =====
function spawnOverworldEnemies() {
  for (const c of CAMPS) {
    const gid = ++GID, lvl = lvlAt(c.x, c.z, c.realm);
    c.kind.forEach((t, i) => { const a = i / c.kind.length * TAU + WR(0, 1), rr = WR(2.5, 6); spawnOW(t, c.x + Math.cos(a) * rr, c.z + Math.sin(a) * rr, lvl + (i === 0 && c.kind.length < 4 ? 1 : 0), gid); });
  }
  // quest packs
  let g = ++GID;
  for (let i = 0; i < 5; i++) spawnOW('wolf', -54 + WR(-6, 6), 30 + WR(-6, 6), 1 + (i % 2), g);
  g = ++GID; const sp = poi('stones');
  ['wraith', 'wraith', 'wraith', 'skeleton', 'skeleton', 'hound', 'skeleton'].forEach((t, i) => { const a = i / 7 * TAU, rr = 13; spawnOW(t, sp.x + Math.cos(a) * rr, sp.z + Math.sin(a) * rr, 2 + (i % 2), g); });
  g = ++GID; for (let i = 0; i < 3; i++) spawnOW('bandit', 48 + WR(-4, 4), -46 + WR(-4, 4), 2, g);
  const dr = poi('drake'); const dg = spawnOW('dragon', dr.x + 2, dr.z + 2, 10, ++GID); if (dg) dg.noLeash = true;
}

// ===== genCamps =====
function genCamps() {
  const k0 = [['bandit', 'bandit', 'bandit', 'archer'], ['goblin', 'goblin', 'goblin', 'goblin'], ['wolf', 'wolf', 'wolf', 'wolf'], ['brute', 'goblin', 'goblin'], ['bandit', 'archer', 'archer', 'brute']];
  const k1 = [['skeleton', 'skeleton', 'skeleton', 'wraith'], ['hound', 'hound', 'hound'], ['wraith', 'wraith', 'wraith'], ['revenant', 'skeleton', 'skeleton'], ['skeleton', 'wraith', 'hound', 'hound']];
  const cnt = [0, 0], need = [15, 13]; let tries = 0;
  while ((cnt[0] < need[0] || cnt[1] < need[1]) && tries++ < 5000) {
    const x = WR(-HALF + 36, HALF - 36), z = WR(-HALF + 36, HALF - 36), d = Math.hypot(x, z);
    if (d < 62) continue; if (!freeSpot(x, z, 8)) continue;
    let bad = false; for (const c of CAMPS) if (Math.hypot(c.x - x, c.z - z) < 48) { bad = true; break; } if (bad) continue;
    const r = cnt[0] <= cnt[1] * 1.15 ? 0 : 1; if (cnt[r] >= need[r]) continue;
    const kinds = r ? k1 : k0, type = kinds[Math.min(kinds.length - 1, Math.floor(d / 70 + WR(0, 2.2)) % kinds.length)];
    CAMPS.push({ x, z, realm: r, kind: type }); cnt[r]++;
  }
}

// ===== loadData =====
function loadData() {
  try { const s = localStorage.getItem(SAVE_KEY); if (s) { const o = JSON.parse(s), n = newData(); const d = Object.assign(n, o); d.skills = Object.assign(n.skills, o.skills || {}); d.eq = Object.assign({ weapon: null, armor: null, shield: null, trinket: null }, o.eq || {}); d.disc = o.disc || { village: 1 }; if (!Array.isArray(o.tree)) { d.tree = []; let r = 0; for (const k in (o.skills || {})) r += o.skills[k] | 0; d.skillPts = (o.skillPts || 0) + r; d.skills = Object.assign({}, n.skills); } return d; } } catch (e) { }
  return newData();
}

// ===== dropLoot =====
function dropLoot(x, z, level, itemChance, bonus, gold, potChance) {
  if (gold > 0) spawnLoot(x, z, { gold });
  if (Math.random() < itemChance) spawnLoot(x, z, { item: rollItem(level, bonus) });
  if (Math.random() < (potChance || 0)) spawnLoot(x, z, { pot: Math.random() < .6 ? 'hp' : 'mp' });
}

// ===== killEnemy =====
function killEnemy(e) {
  e.dead = true; e.state = 'dead'; e.deadT = 0; e.hp = 0; e.atkP = -1; D.kills++;
  const d = e.def, lvlF = e.lvl < D.level - 4 ? .5 : 1, xp = Math.round(d.xp * (1 + .12 * (e.lvl - 1)) * lvlF * (e.summoned ? .3 : 1));
  const ups = gainXp(xp); floatAt(e, '+' + xp + ' xp', '#6ec1ff');
  if (ups) { onLevelUp(); }
  burst(e.x, e.y + 1, e.z, e.def.kind === 'wraith' ? 30 : 16, 5, .6, .5, e.def.kind === 'wraith' ? .7 : .8, e.def.kind === 'wraith' ? .4 : .2, e.def.kind === 'wraith' ? 1 : .2, 2);
  if (!e.summoned) {
    const gold = RI(2 + e.lvl, 6 + e.lvl * 2) * (d.boss ? 12 : 1);
    const chance = d.boss ? 1 : e.arena ? .12 : .2 + (e.realm ? .06 : 0), bonus = (d.boss ? 2 : 0) + (e.realm ? 1 : 0);
    dropLoot(e.x, e.z, e.lvl, chance, bonus, gold, d.boss ? .6 : .07);
    if (d.boss) { dropLoot(e.x + 1, e.z + 1, e.lvl, 1, 3, 0, 0); dropLoot(e.x - 1, e.z - 1, e.lvl, .6, 2, 0, 0); }
  }
  e.respawn = e.arena || e.summoned || Z.kind === 'dun' ? 0 : (d.boss ? 600 : 260);
  if (e.arena) { A.alive--; if (crowd) crowd.cheer = 1; }
  questEvent('kill', { id: e.type, realm: e.realm, boss: !!d.boss });
  if (d.boss) { banner(d.n, 'has fallen'); Sfx.level(); if (Z.kind === 'dun') onDungeonBossDown(e); }
  if (P.target === e) P.targetT = 1.5;
  save();
}

// not found as plain functions: maxHp, maxMp, maxSt, wType, defense, spellPow, xpNeed, saveGame, newData, buyItem, sellValue

// ===== core stat & progression one-liners (01_core.js 36-66) =====
const newData = () => ({
  v: 1, name: 'Hero', level: 1, xp: 0, gold: 60, hpPot: 3, mpPot: 2, str: 5, dex: 5, vit: 5, mag: 5, attrPts: 0, skillPts: 3, tree: [],
  skills: { sword: 0, guard: 0, evoc: 0, resto: 0, athl: 0 },
  eq: { weapon: { name: 'Iron Sword', slot: 'weapon', wt: 'sword', rar: 0, lvl: 1, dmg: 4, def: 0, hp: 0, mp: 0 }, armor: null, shield: { name: 'Oak Buckler', slot: 'shield', rar: 0, lvl: 1, dmg: 0, def: 2, hp: 0, mp: 0 }, trinket: null },
  inv: [], quests: {}, kills: 0, bestWave: 0, bestDuel: 0, deepest: 0, pos: null, time: .33, disc: { village: 1 }, horse: false, realm: 0, played: 0
});
let D = loadData();
function loadData() {
  try { const s = localStorage.getItem(SAVE_KEY); if (s) { const o = JSON.parse(s), n = newData(); const d = Object.assign(n, o); d.skills = Object.assign(n.skills, o.skills || {}); d.eq = Object.assign({ weapon: null, armor: null, shield: null, trinket: null }, o.eq || {}); d.disc = o.disc || { village: 1 }; if (!Array.isArray(o.tree)) { d.tree = []; let r = 0; for (const k in (o.skills || {})) r += o.skills[k] | 0; d.skillPts = (o.skillPts || 0) + r; d.skills = Object.assign({}, n.skills); } return d; } } catch (e) { }
  return newData();
}
function save() { try { localStorage.setItem(SAVE_KEY, JSON.stringify(D)); } catch (e) { } }
const hasSave = () => { try { return !!localStorage.getItem(SAVE_KEY); } catch (e) { return false; } };
const eqStat = k => { let s = 0; for (const i of Object.values(D.eq)) if (i) s += i[k] || 0; return s; };
const maxHp = () => Math.max(30, Math.round((100 + D.level * 8 + D.vit * 7 + eqStat('hp') + TS.hp) * (1 + TS.hpP / 100)));
const maxMp = () => Math.round((50 + D.mag * 6 + D.level * 2 + eqStat('mp') + TS.mp) * (1 + TS.mpP / 100));
const maxSt = () => Math.round(90 + D.dex * 3 + TS.st);
const wType = () => (D.eq.weapon && D.eq.weapon.wt) || 'sword';
function wpnDmg() {
  const wt = wType(), w = D.eq.weapon, wd = w ? w.dmg : 0, all = 1 + TS.allP / 100;
  if (wt === 'bow') return (5 + D.dex * 1.7 + wd + D.str * TS.strToBow + D.mag * TS.magToBow) * (1 + TS.rangedP / 100) * all;
  if (wt === 'wand' || wt === 'staff') return (4 + D.mag * 1.5 + wd) * (1 + TS.spellP / 100) * (1 + eqStat('sp')) * all;
  return (5 + D.str * 1.8 + wd + D.mag * TS.magToDmg) * (1 + TS.meleeP / 100) * all;
}
const defense = () => eqStat('def') + Math.floor(D.vit / 2) + TS.def;
const spellPow = () => (1 + D.mag * .08 + D.level * .02) * (1 + TS.spellP / 100) * (1 + eqStat('sp')) * (1 + TS.allP / 100);
const xpNeed = () => 80 + (D.level - 1) * 55 + (D.level - 1) * (D.level - 1) * 8;
function gainXp(n) {
  D.xp += n; let up = 0;
  while (D.xp >= xpNeed()) { D.xp -= xpNeed(); D.level++; D.attrPts += 2; D.skillPts += 2; up++; }
  return up;
// ===== shop pricing (05_zones.js) =====
  openUI(`<h2>${esc(n.name)}</h2><p class="say">${text}</p><div class="opts">${opts.map((o, i) => `<button class="btn" data-a="say" data-v="${i}">${o.label}</button>`).join('')}</div>`, { say: i => { Sfx.ui(); SAYACT[+i].fn(); } });
}
const SHOP = { lvl: -1, items: [] };
function ensureStock() { if (SHOP.lvl !== D.level) { SHOP.lvl = D.level; SHOP.items = []; for (let i = 0; i < 6; i++) SHOP.items.push(rollItem(D.level + 1, .2)); } }
const buyPrice = it => itemValue(it) * 3 + 12;
function shopUI(tab) {
  ensureStock(); tab = tab || 'buy';
const itemValue = it => Math.max(1, Math.round((it.dmg * 4 + it.def * 4 + it.hp * .6 + it.mp * .6) * (1 + it.rar * .8)));
