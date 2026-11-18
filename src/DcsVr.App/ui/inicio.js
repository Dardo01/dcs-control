'use strict';
/* DCS Control, interfaz sencilla: Volar (2D o VR, ReShade y VOLAR) y Ajustes (programas, ReShade, rendimiento, DCS). */
const $ = id => document.getElementById(id);
const escape = v => String(v ?? '').replace(/[&<>"']/g,c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let view = null, busy = false, sequence = 0, saveTimer = null;
const pending = new Map();

function request(action, data = {}) {
  return new Promise((resolve,reject) => {
    if (!window.chrome?.webview) { reject(new Error('Abre esta página desde DCS Control.')); return; }
    const id = String(++sequence);
    pending.set(id,{resolve,reject}); window.chrome.webview.postMessage({id,action,data});
  });
}
window.chrome?.webview?.addEventListener('message',e => {
  const item = pending.get(e.data.id); if (!item) return; pending.delete(e.data.id);
  e.data.ok ? item.resolve(e.data.data) : item.reject(new Error(e.data.error));
});
let toastTimer;
function showError(error) { $('toast').textContent = error.message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true,9000); }

const lines = text => String(text || '').split(/\r?\n/).map(l => l.trim()).filter(Boolean);
const luaText = settings => Object.entries(settings || {}).map(([k,v]) => k+' = '+v).join('\n');
function parseLua(text) {
  const result = {};
  for (const line of lines(text)) {
    const at = line.indexOf('=');
    if (at < 1) throw new Error('Cada ajuste va en una línea "ruta = valor": '+line);
    result[line.slice(0,at).trim()] = line.slice(at+1).trim();
  }
  return result;
}
const mode = id => view.set.modes.find(m => m.id === id);
const fileName = path => String(path || '').split('\\').pop();

/* ---------- Volar ---------- */
function renderVolar() {
  const set = view.set, selected = set.lastModeId;
  document.querySelectorAll('.mode').forEach(b => {
    b.setAttribute('aria-checked',String(b.dataset.mode === selected));
    const m = mode(b.dataset.mode), apps = (m?.apps || []).filter(a => a.enabled).map(a => a.name);
    const fx = m?.vrem && view.vremState ? '<span class="fx">VREM</span>' : '';
    b.querySelector('.apps').innerHTML = apps.map(a => '<span>'+escape(a)+'</span>').join('')+fx;
  });
  const state = view.reShadeState, found = Boolean(view.reShadeFound);
  $('reShade').checked = state ? (set.reShadeOn ?? state === 'on') : false;
  $('reShade').disabled = !found;
  $('reShade').closest('.toggle').classList.toggle('missing',!found);
  $('reShadeState').textContent = !found ? 'No se encuentra junto a DCS · revisa Ajustes'
    : $('reShade').checked ? 'Activado en 2D y en VR' : 'Desactivado en 2D y en VR';
  $('fly').disabled = busy || view.dcsRunning || !view.dcs;
  $('flyText').textContent = view.dcsRunning ? 'EN VUELO' : busy ? 'INICIANDO…' : 'VOLAR';
  const led = $('readyLed'); led.className = 'led '+(view.dcsRunning ? 'run' : view.dcs ? 'ok' : 'bad');
  $('readyText').textContent = view.dcsRunning ? 'DCS en marcha' : view.dcs ? 'DCS listo · '+(selected === 'vr' ? 'Quest Link' : 'Monitor') : 'No se encuentra DCS · revisa Ajustes';
  const o = view.optimizations;
  $('strip').innerHTML = [['Motor optimizado',o.engineOptimizations],['Prioridad de CPU',o.cpuBoost],['VRAM libre',o.freeVram],['ReShade',found && $('reShade').checked]]
    .map(([t,on]) => `<span class="${on ? 'on' : ''}"><i></i>${t}</span>`).join('');
  $('notes').innerHTML = (view.notes || []).map(n => '<li>'+escape(n)+'</li>').join('');
}

async function fly() {
  if (busy) return;
  busy = true; $('status').classList.remove('error'); $('status').textContent = 'Preparando DCS…'; renderVolar();
  try { await flushSave(); view = await request('fly',{mode:view.set.lastModeId}); $('status').textContent = ''; }
  catch (error) { $('status').classList.add('error'); $('status').textContent = error.message; }
  finally { busy = false; renderVolar(); }
}

/* ---------- Ajustes ---------- */
function renderAjustes() {
  const set = view.set;
  $('apps').innerHTML = set.modes.map(m => `<div class="group" data-mode="${escape(m.id)}"><h3>Al volar en <span>${escape(m.name)}</span></h3>
    ${(m.apps || []).map((a,i) => `<div class="app${a.enabled ? '' : ' off'}" data-index="${i}">
      <input type="checkbox" data-app="enabled" ${a.enabled ? 'checked' : ''} title="Abrir con DCS">
      <span class="ico" aria-hidden="true">${escape((a.name || '?').slice(0,1).toUpperCase())}</span>
      <div><div class="name">${escape(a.name)}</div><div class="where" title="${escape(a.path)}">${escape(a.path)}</div></div>
      <button class="small" data-app="change">Cambiar…</button><button class="icon" data-app="remove" title="Quitar">✕</button></div>`).join('') || '<p class="empty">Ningún programa todavía.</p>'}
    <button class="small add" data-app="add">+ Añadir programa</button></div>`).join('');
  $('optEngine').checked = view.optimizations.engineOptimizations; $('optBoost').checked = view.optimizations.cpuBoost; $('optVram').checked = view.optimizations.freeVram;
  $('reShadePath').value = set.reShade?.dllPath || '';
  $('reShadeFound').className = 'found'+(view.reShadeFound ? '' : ' none');
  $('reShadeFound').textContent = view.reShadeFound ? 'Encontrado: '+view.reShadeFound+(view.reShadeState === 'off' ? ' (desactivado)' : '') : 'No se ha encontrado ReShade junto a DCS.exe.';
  const vremCount = (view.vremFound || []).length;
  $('vremFound').className = 'found'+(vremCount ? '' : ' none');
  $('vremFound').textContent = vremCount ? 'Instalado ('+vremCount+(vremCount === 1 ? ' carpeta' : ' carpetas')+')'+(view.vremState === 'off' ? ', ahora desactivado.' : ', ahora activado.') : 'VREM no está instalado junto a DCS.';
  $('vrem2d').checked = Boolean(mode('2d')?.vrem); $('vremvr').checked = Boolean(mode('vr')?.vrem);
  $('dcsPath').value = view.dcs || ''; $('optionsPath').value = view.options || '';
  $('lua2d').value = luaText(mode('2d')?.dcsSettings); $('luavr').value = luaText(mode('vr')?.dcsSettings);
  const odt = mode('vr')?.oculusDebug || {};
  $('odtOn').checked = Boolean(odt.enabled); $('odtBefore').value = (odt.beforeFlight || []).join('\n'); $('odtAfter').value = (odt.afterFlight || []).join('\n'); $('odtPath').value = odt.cliPath || '';
}

/** The settings fields back into view.set (the programs are changed in place by their buttons). */
function readAjustes() {
  const set = view.set;
  set.reShade = {...(set.reShade || {}), dllPath: $('reShadePath').value.trim()};
  set.modes = set.modes.map(m => {
    const next = {...m};
    if (m.id === '2d') { next.dcsSettings = parseLua($('lua2d').value); next.vrem = $('vrem2d').checked; }
    if (m.id === 'vr') { next.dcsSettings = parseLua($('luavr').value); next.vrem = $('vremvr').checked;
      next.oculusDebug = {...(m.oculusDebug || {}), enabled: $('odtOn').checked, beforeFlight: lines($('odtBefore').value), afterFlight: lines($('odtAfter').value), cliPath: $('odtPath').value.trim()}; }
    return next;
  });
}

async function save() {
  clearTimeout(saveTimer); saveTimer = null;
  const payload = {set: view.set, optimizations: {engineOptimizations: $('optEngine').checked, cpuBoost: $('optBoost').checked, freeVram: $('optVram').checked},
    dcs: $('dcsPath').value.trim(), options: $('optionsPath').value.trim()};
  view = await request('simpleSave',payload);
  $('saved').textContent = 'Guardado';
  setTimeout(() => $('saved').textContent = '',2500);
  renderVolar();
}
function saveSoon() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => { try { readAjustes(); save().catch(showError); } catch (error) { showError(error); } },700);
}
async function flushSave() { if (saveTimer) { readAjustes(); await save(); } }

async function browse(target) {
  const kind = {dcsPath:'dcs', optionsPath:'options', odtPath:'program', reShadePath:'dll'}[target] || 'program';
  const picked = await request('browse',{target:kind});
  return picked?.path || null;
}

/* ---------- Arranque ---------- */
function showTab(tab) {
  document.querySelectorAll('[data-tab]').forEach(b => b.setAttribute('aria-selected',String(b.dataset.tab === tab)));
  $('volar').hidden = tab !== 'volar'; $('ajustes').hidden = tab !== 'ajustes'; document.body.dataset.tab = tab;
  if (tab === 'volar') flushSave().catch(showError);
}

async function load(rescan) {
  try { view = await request('simpleState',{rescan: Boolean(rescan)}); renderVolar(); renderAjustes(); }
  catch (error) { $('status').classList.add('error'); $('status').textContent = error.message; }
}

function setup() {
  document.querySelectorAll('[data-tab]').forEach(b => b.addEventListener('click',() => showTab(b.dataset.tab)));
  document.querySelectorAll('[data-window]').forEach(b => b.addEventListener('click',() => request('window',{op:b.dataset.window}).catch(showError)));
  document.querySelectorAll('.mode').forEach(b => b.addEventListener('click',() => {
    if (!view || busy) return; view.set.lastModeId = b.dataset.mode; renderVolar(); save().catch(showError);
  }));
  $('reShade').addEventListener('change',e => { if (!view) return; view.set.reShadeOn = e.target.checked; renderVolar(); save().catch(showError); });
  $('fly').addEventListener('click',fly);
  $('vremLink').addEventListener('click',e => { e.preventDefault(); request('openVrem').catch(showError); });
  document.addEventListener('keydown',e => { if (e.key === 'Enter' && document.body.dataset.tab === 'volar' && !e.target.closest('input,textarea') && !$('fly').disabled) { e.preventDefault(); fly(); } });
  // Ajustes: the side list follows the section in view.
  document.querySelectorAll('.side a').forEach(a => a.addEventListener('click',e => { e.preventDefault(); document.querySelector(a.getAttribute('href')).scrollIntoView({behavior:'smooth'}); }));
  document.querySelector('main').addEventListener('scroll',() => {
    if (document.body.dataset.tab !== 'ajustes') return;
    let current = 'programas';
    document.querySelectorAll('.card').forEach(c => { if (c.getBoundingClientRect().top < 160) current = c.id.slice(2); });
    document.querySelectorAll('.side a').forEach(a => a.classList.toggle('on',a.getAttribute('href') === '#s-'+current));
  });

  $('ajustes').addEventListener('input',e => { if (e.target.closest('.app') || e.target.type === 'checkbox') return; saveSoon(); });
  $('ajustes').addEventListener('click',async e => {
    const button = e.target.closest('button'); if (!button || !view) return;
    try {
      if (button.dataset.browse) { const path = await browse(button.dataset.browse); if (path) { $(button.dataset.browse).value = path; readAjustes(); await save(); renderAjustes(); } return; }
      if (button.id === 'reset') { view = await request('simpleReset'); renderVolar(); renderAjustes(); return; }
      const group = button.closest('.group'); if (!group) return;
      const m = mode(group.dataset.mode), row = button.closest('.app'), index = row ? Number(row.dataset.index) : -1;
      if (button.dataset.app === 'add' || button.dataset.app === 'change') {
        const path = await browse('program'); if (!path) return;
        const app = {name: fileName(path).replace(/\.exe$/i,''), path, enabled: true, closeOnExit: true, delaySeconds: 2};
        m.apps = button.dataset.app === 'add' ? [...(m.apps || []), app] : m.apps.map((a,i) => i === index ? {...a, name: app.name, path} : a);
      }
      if (button.dataset.app === 'remove') m.apps = m.apps.filter((_,i) => i !== index);
      readAjustes(); await save(); renderAjustes();
    } catch (error) { showError(error); }
  });
  $('ajustes').addEventListener('change',e => {
    if (!view) return;
    if (e.target.dataset.app === 'enabled') {
      const m = mode(e.target.closest('.group').dataset.mode), index = Number(e.target.closest('.app').dataset.index);
      m.apps = m.apps.map((a,i) => i === index ? {...a, enabled: e.target.checked} : a);
      readAjustes(); save().catch(showError); return;
    }
    if (e.target.type === 'checkbox') { try { readAjustes(); save().catch(showError); } catch (error) { showError(error); } }
  });
  // DCS started or closed: the VOLAR button follows.
  setInterval(async () => {
    if (busy || !view || document.hidden) return;
    try { const s = await request('dcsStatus'); if (s.dcsRunning !== view.dcsRunning) { view.dcsRunning = s.dcsRunning; if (!s.dcsRunning) view = await request('simpleState'); renderVolar(); } } catch {}
  },4000);
  load(false);
}
setup();
