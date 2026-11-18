'use strict';
/* Volar: the 2D and VR flight modes (FlightModes.cs). Each mode writes its own DCS settings, turns the DLSS 5 DLL on
   or off, runs Oculus Debug Tool commands and opens its programs, then starts DCS in 2D or VR. */
let flySet = null, flyDlss5On = false, flyNotes = [], flyDirty = false;

const flyLines = text => String(text || '').split(/\r?\n/).map(l => l.trim()).filter(Boolean);
const flySettingsText = settings => Object.entries(settings || {}).map(([k,v]) => k+' = '+v).join('\n');
function flyParseSettings(text) {
  const result = {};
  for (const line of flyLines(text)) {
    const at = line.indexOf('=');
    if (at < 1) throw new Error('Cada ajuste de DCS va en una línea "ruta = valor": '+line);
    result[line.slice(0,at).trim()] = line.slice(at+1).trim();
  }
  return result;
}

async function loadFly() {
  try {
    const reply = await request('flightModes');
    flySet = reply.set; flyDlss5On = reply.dlss5On; flyNotes = reply.notes || []; flyDirty = false;
    renderFly();
  } catch (error) { showError(error); }
}

function flySummary(mode) {
  const apps = (mode.apps || []).filter(a => a.enabled).map(a => a.name);
  const parts = [mode.kind === 'Vr' ? 'Quest Link' : 'Monitor', 'DLSS 5 '+(mode.dlss5 ? 'activado' : 'desactivado')];
  if (apps.length) parts.push('abre '+apps.join(', '));
  if (mode.kind === 'Vr' && mode.oculusDebug?.enabled) parts.push('Oculus Debug Tool');
  return parts.join(' · ');
}

function renderFly() {
  if (!$('page-fly')) return;
  $('flyAdvanced').checked = advancedPages;
  $('flyDesktopNote').hidden = !profile || Boolean(profile.desktop);
  if (!flySet) { $('flyButtons').innerHTML = '<p class="fine">Cargando modos…</p>'; return; }
  $('flyButtons').innerHTML = flySet.modes.map(m => `<button class="fly-button${m.id === flySet.lastModeId ? ' last' : ''}" data-fly="${escape(m.id)}" ${busy || state?.dcsRunning ? 'disabled' : ''}>
      <span class="fly-icon" aria-hidden="true">${m.kind === 'Vr' ? '◉◉' : '▭'}</span><b>${escape(m.name)}</b><small>${escape(flySummary(m))}</small></button>`).join('');
  $('flyNotes').hidden = !flyNotes.length;
  $('flyNotes').innerHTML = '<h2>Último lanzamiento</h2><ul>'+flyNotes.map(n => '<li>'+escape(n)+'</li>').join('')+'</ul>';
  $('flyModes').innerHTML = flySet.modes.map((m,i) => `<div class="card fly-mode" data-mode="${i}">
    <div class="card-heading"><div><h2>${escape(m.name)}</h2><p class="fine">DCS arranca ${m.kind === 'Vr' ? 'en VR (--force_enable_VR)' : 'sin VR (--force_disable_VR)'}.</p></div></div>
    <label class="switch-row"><span>DLSS 5 en este modo</span><input type="checkbox" data-field="dlss5" ${m.dlss5 ? 'checked' : ''}></label>
    <h3>Programas que se abren con DCS</h3>
    <p class="fine">Se abren antes que DCS y, si los abrió la app, se cierran al salir.</p>
    ${(m.apps || []).map((a,j) => `<div class="fly-app" data-app="${j}">
      <input type="checkbox" data-app-field="enabled" ${a.enabled ? 'checked' : ''} title="Activado">
      <input data-app-field="name" value="${escape(a.name)}" placeholder="Nombre" class="fly-app-name">
      <input data-app-field="path" value="${escape(a.path)}" placeholder="C:\\…\\programa.exe" class="fly-app-path">
      <button class="button secondary" data-app-browse="${j}">Buscar…</button><button class="icon-button" data-app-remove="${j}" title="Quitar">✕</button></div>`).join('')}
    <button class="button secondary" data-app-add>Añadir programa</button>
    <details><summary>Ajustes de DCS (options.lua)</summary>
      <p class="fine">Una línea por ajuste, "ruta = valor" en Lua. Solo se cambian los que ya existen en options.lua.</p>
      <textarea data-field="dcsSettings" rows="7" spellcheck="false">${escape(flySettingsText(m.dcsSettings))}</textarea></details>
    ${m.kind === 'Vr' ? `<details><summary>Oculus Debug Tool</summary>
      <label class="switch-row"><span>Aplicar comandos de Oculus Debug Tool</span><input type="checkbox" data-field="odtEnabled" ${m.oculusDebug?.enabled ? 'checked' : ''}></label>
      <p class="fine">Antes de volar (uno por línea):</p><textarea data-field="odtBefore" rows="3" spellcheck="false">${escape((m.oculusDebug?.beforeFlight || []).join('\n'))}</textarea>
      <p class="fine">Al terminar:</p><textarea data-field="odtAfter" rows="3" spellcheck="false">${escape((m.oculusDebug?.afterFlight || []).join('\n'))}</textarea>
      <p class="fine">OculusDebugToolCLI.exe (vacío: se busca solo):</p><input data-field="odtPath" value="${escape(m.oculusDebug?.cliPath || '')}" class="fly-app-path"></details>` : ''}
  </div>`).join('');
  const d = flySet.dlss5 || {};
  $('flyDlss').innerHTML = `<div class="card-heading"><div><h2>DLL de DLSS 5</h2><p class="fine">Ahora: ${d.targetPath && d.replacementPath ? (flyDlss5On ? '<b>activado</b>' : 'desactivado') : 'sin configurar'}.
    Al activarlo, tu DLL sustituye al de DCS; al desactivarlo vuelve el original (se guarda una copia la primera vez).</p></div></div>
    <label class="setup-path">DLL que carga DCS<div><input id="flyDllTarget" value="${escape(d.targetPath || '')}" placeholder="…\\DCSWorld\\bin-mt\\nvngx_dlss.dll"><button class="button secondary" data-dll-browse="target">Buscar…</button></div></label>
    <label class="setup-path">Tu DLL de DLSS 5<div><input id="flyDllReplacement" value="${escape(d.replacementPath || '')}" placeholder="…\\nvngx_dlss.dll"><button class="button secondary" data-dll-browse="replacement">Buscar…</button></div></label>`;
}

/** The page's fields back into flySet. */
function readFly() {
  document.querySelectorAll('.fly-mode').forEach(card => {
    const i = Number(card.dataset.mode), m = flySet.modes[i], q = f => card.querySelector(`[data-field="${f}"]`);
    const apps = [...card.querySelectorAll('.fly-app')].map(row => {
      const j = Number(row.dataset.app), get = f => row.querySelector(`[data-app-field="${f}"]`);
      return {...m.apps[j], enabled: get('enabled').checked, name: get('name').value.trim(), path: get('path').value.trim()};
    });
    const next = {...m, dlss5: q('dlss5').checked, apps, dcsSettings: flyParseSettings(q('dcsSettings').value)};
    if (m.kind === 'Vr') next.oculusDebug = {...(m.oculusDebug || {}), enabled: q('odtEnabled').checked, beforeFlight: flyLines(q('odtBefore').value), afterFlight: flyLines(q('odtAfter').value), cliPath: q('odtPath').value.trim()};
    flySet.modes[i] = next;
  });
  flySet.dlss5 = {...(flySet.dlss5 || {}), targetPath: $('flyDllTarget').value.trim(), replacementPath: $('flyDllReplacement').value.trim()};
}

async function saveFly(quiet) {
  readFly();
  await request('flightModesSave',{set: flySet});
  flyDirty = false;
  if (!quiet) { $('toast').textContent = 'Modos guardados.'; $('toast').hidden = false; setTimeout(() => $('toast').hidden = true,4000); }
}

async function fly(id) {
  if (busy) return;
  try { if (flyDirty) await saveFly(true); } catch (error) { showError(error); return; }
  try { await run('fly',{mode:id}); } catch { return; }
  await loadFly();
}

function setupFly() {
  const pageEl = $('page-fly'); if (!pageEl) return;
  pageEl.addEventListener('input',() => { flyDirty = true; });
  pageEl.addEventListener('change',e => { if (e.target.id !== 'flyAdvanced') flyDirty = true; });
  pageEl.addEventListener('click',async e => {
    const t = e.target.closest('button'); if (!t) return;
    try {
      if (t.dataset.fly) return fly(t.dataset.fly);
      if (t.id === 'flySave') return saveFly(false);
      if (t.id === 'flyReset') { await request('flightModesReset'); return loadFly(); }
      if (t.id === 'flyUseDesktop') { profile.desktop = true; showProfile?.(['desktop']); change(); applyRail(); return renderFly(); }
      const card = t.closest('.fly-mode');
      if (card && t.hasAttribute('data-app-add')) { readFly(); flySet.modes[Number(card.dataset.mode)].apps.push({name:'',path:'',enabled:true,closeOnExit:true,delaySeconds:2}); flyDirty = true; return renderFly(); }
      if (card && t.dataset.appRemove !== undefined) { readFly(); flySet.modes[Number(card.dataset.mode)].apps.splice(Number(t.dataset.appRemove),1); flyDirty = true; return renderFly(); }
      if (card && t.dataset.appBrowse !== undefined) {
        const picked = await request('browse',{target:'program'}); if (!picked.path) return;
        readFly(); const app = flySet.modes[Number(card.dataset.mode)].apps[Number(t.dataset.appBrowse)];
        app.path = picked.path; if (!app.name) app.name = picked.path.split('\\').pop().replace(/\.exe$/i,''); flyDirty = true; return renderFly();
      }
      if (t.dataset.dllBrowse) {
        const picked = await request('browse',{target:'dll'}); if (!picked.path) return;
        readFly(); flySet.dlss5[t.dataset.dllBrowse === 'target' ? 'targetPath' : 'replacementPath'] = picked.path; flyDirty = true; return renderFly();
      }
    } catch (error) { showError(error); }
  });
  $('flyAdvanced').addEventListener('change',e => {
    advancedPages = e.target.checked; try { localStorage.setItem('advancedPages',advancedPages ? '1' : '0'); } catch {}
    applyRail();
  });
  loadFly();
}
window.renderFly = renderFly;
setupFly();
