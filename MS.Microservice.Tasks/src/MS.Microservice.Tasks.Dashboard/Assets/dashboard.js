'use strict';

const $ = id => document.getElementById(id);
const stateNames = ['等待处理', '正在执行', '执行成功', '执行失败', '已取消', '执行中断'];
const stateClasses = ['queued', 'running', 'succeeded', 'failed', 'cancelled', 'interrupted'];
const dateFormat = new Intl.DateTimeFormat('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
let token = '', session = null, jobs = [], selectedId = null, view = 'jobs', busy = false, mutation = false;
let generation = 0, detailRequest = 0, refreshAgain = false, requestAbort = new AbortController(), pendingAction = null;
const apiBase = new URL('api/', location.href);

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined && text !== null) node.textContent = String(text);
  if (className) node.className = className;
  return node;
}
function date(value) { return value ? dateFormat.format(new Date(value)) : '—'; }
function showNotice(message) { $('notice').textContent = message; $('notice').hidden = !message; }
function status(job) { return job.state === 1 && job.cancellationRequested ? '取消中' : stateNames[job.state] ?? '未知状态'; }
function badge(job) { return element('span', status(job), 'badge ' + (stateClasses[job.state] ?? 'neutral')); }
function canCancel(job) { return session?.canControl && [0, 1].includes(job.state) && !job.cancellationRequested; }
function canRetry(job) { return session?.canControl && [3, 4, 5].includes(job.state) && job.attemptCount < job.maximumAttempts; }
function report(error) {
  if (error.name === 'AbortError') return;
  if (error.status === 401) {
    resetConnection();
    showNotice('请在宿主登录，或重新连接 Bearer 令牌。');
  } else showNotice(error.message || '请求未完成，请稍后刷新。');
}
async function request(path, method = 'GET') {
  const version = generation;
  const headers = { Accept: 'application/json' };
  if (token) headers.Authorization = 'Bearer ' + token;
  if (method !== 'GET') headers[session.csrfHeaderName] = session.csrfRequestToken;
  const response = await fetch(new URL(path, apiBase), { method, headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: requestAbort.signal });
  if (version !== generation) throw new DOMException('连接已切换', 'AbortError');
  if (!response.ok) {
    let message = response.status === 403 ? '当前身份无权执行此操作。' : response.status === 404 ? '任务不存在或不属于当前身份。' : '请求未完成（' + response.status + '）。';
    try { const problem = await response.json(); message = problem.detail || message; } catch { /* A host may return an HTML error page; keep the generic message. */ }
    const error = new Error(message); error.status = response.status; throw error;
  }
  const data = response.status === 204 ? null : await response.json();
  if (version !== generation) throw new DOMException('连接已切换', 'AbortError');
  return data;
}
function resetConnection() {
  generation++; requestAbort.abort(); requestAbort = new AbortController();
  token = ''; session = null; jobs = []; selectedId = null; busy = false; mutation = false; refreshAgain = false; detailRequest++;
  $('bearer-token').value = ''; $('connection-dot').classList.remove('connected');
  $('connection-label').textContent = '未连接'; $('auth-open').textContent = '连接'; $('updated-at').textContent = '等待连接';
  $('nav-count').textContent = '—'; $('list-count').textContent = '—';
  for (const name of ['running', 'queued', 'succeeded', 'failed']) $('stat-' + name).textContent = '—';
  $('jobs-body').replaceChildren(); $('schedules-body').replaceChildren(); $('schedule-count').textContent = '—';
  $('lookup-id').value = ''; $('filter-text').value = ''; $('jobs-body').dataset.hasMore = 'false';
  $('window-note').textContent = '结果按当前登录身份隔离';
  $('detail-title').textContent = '任务详情'; $('refresh').disabled = false; $('confirm-yes').disabled = false;
  $('jobs-empty').hidden = false; $('schedules-empty').hidden = false;
  $('jobs-empty').querySelector('h3').textContent = '连接后查看任务';
  $('jobs-empty').querySelector('p').textContent = '使用宿主登录身份，或输入 Bearer 令牌。';
  $('detail-dialog').close(); $('confirm-dialog').close(); pendingAction = null;
  $('detail-content').replaceChildren();
}
async function connect(bearer = '') {
  resetConnection(); token = bearer; showNotice(''); $('connect').disabled = true; $('auth-error').hidden = true;
  const version = generation;
  try {
    session = await request('session');
    document.title = session.title;
    document.querySelectorAll('[data-title]').forEach(node => node.textContent = session.title);
    $('nav-schedules').hidden = !session.canViewSchedules;
    $('connection-dot').classList.add('connected'); $('connection-label').textContent = '已连接'; $('auth-open').textContent = '切换连接';
    $('auth-dialog').close();
    if (view === 'schedules' && !session.canViewSchedules) switchView('jobs');
    await refresh();
  } catch (error) {
    if (version === generation && error.name !== 'AbortError') {
      const message = error.status === 401 ? '尚未通过身份验证，请确认宿主登录或令牌后重试。' : error.message;
      report(error); $('auth-error').textContent = message; $('auth-error').hidden = false;
    }
  }
  finally { if (version === generation || !session) $('connect').disabled = false; }
}
function progress(job) {
  const wrapper = element('div'), line = element('div', null, 'progress-line');
  const bar = element('progress', null, stateClasses[job.state]); bar.max = 100; bar.value = job.progress;
  bar.setAttribute('aria-label', '执行进度');
  line.append(bar, element('span', job.progress + '%', 'progress-number')); wrapper.append(line);
  if (job.progressMessage) wrapper.append(element('div', job.progressMessage, 'progress-message'));
  return wrapper;
}
function actions(job) {
  const group = element('div', null, 'row-actions');
  for (const [action, label, allowed] of [['cancel', '取消', canCancel(job)], ['retry', '重试', canRetry(job)]]) {
    if (!allowed) continue;
    const button = element('button', label, 'button small ' + (action === 'cancel' ? 'danger' : 'secondary'));
    button.dataset.jobAction = action; button.disabled = mutation;
    button.addEventListener('click', () => confirmAction(job, action)); group.append(button);
  }
  if (!group.childNodes.length) group.append(element('span', '—', 'muted'));
  return group;
}
function renderJobs(hasMore = false) {
  const filter = $('filter-text').value.trim().toLowerCase();
  const visible = jobs.filter(job => !filter || [job.handlerName, job.jobId, job.queue].some(value => value.toLowerCase().includes(filter)));
  $('jobs-body').replaceChildren();
  for (const job of visible) {
    const row = element('tr'), name = element('td');
    const link = element('button', job.handlerName, 'task-name'); link.addEventListener('click', () => openDetail(job.jobId).catch(report));
    name.append(link, element('span', job.jobId.slice(0, 8) + ' · 第 ' + job.attemptCount + ' / ' + job.maximumAttempts + ' 次', 'task-id'));
    const state = element('td'); state.append(badge(job)); const percent = element('td'); percent.append(progress(job));
    const queue = element('td'); queue.append(element('span', job.queue, 'queue-label'));
    const controls = element('td'); controls.append(actions(job));
    row.append(name, state, percent, queue, element('td', date(job.createdAtUtc)), controls); $('jobs-body').append(row);
  }
  $('jobs-empty').hidden = visible.length > 0;
  if (session) {
    $('jobs-empty').querySelector('h3').textContent = filter ? '当前列表中没有匹配任务' : '当前状态下暂无任务';
    $('jobs-empty').querySelector('p').textContent = '可以切换状态筛选，或按完整任务 ID 查看。';
  }
  $('nav-count').textContent = jobs.length; $('list-count').textContent = visible.length;
  for (const [name, state] of [['running', 1], ['queued', 0], ['succeeded', 2], ['failed', 3]])
    $('stat-' + name).textContent = jobs.filter(job => job.state === state).length;
  $('window-note').textContent = (hasMore ? '还有更早任务；' : '') + '当前窗口 ' + jobs.length + ' 条，筛选后 ' + visible.length + ' 条';
}
function renderSchedules(schedules) {
  $('schedules-body').replaceChildren(); $('schedule-count').textContent = schedules.length; $('schedules-empty').hidden = schedules.length > 0;
  for (const plan of schedules) {
    const row = element('tr'), name = element('td');
    name.append(element('div', plan.name, 'schedule-name'), element('div', plan.invocableType || '内联委托', 'schedule-type'));
    const rule = element('td'); rule.append(element('div', plan.interval || '尚未配置频率'), element('div', plan.cron || '', 'task-id'));
    const location = element('td'); location.append(element('div', plan.timeZoneId), element('div', plan.worker, 'task-id'));
    const rules = element('td', null, 'schedule-rules');
    const labels = [...plan.constraints];
    if (plan.preventOverlapping) labels.push('防止重叠'); if (plan.runOnceAtStart) labels.push('启动时执行'); if (plan.runOnce) labels.push('仅一次');
    for (const label of labels) rules.append(element('span', label, 'badge neutral'));
    if (!labels.length) rules.textContent = '—';
    row.append(name, rule, location, rules); $('schedules-body').append(row);
  }
}
async function refresh() {
  if (!session) return;
  if (busy) { refreshAgain = true; return; }
  const version = generation, activeView = view; busy = true; $('refresh').disabled = true;
  try {
    if (activeView === 'jobs') {
      const query = new URLSearchParams({ take: $('take-filter').value });
      if ($('state-filter').value) query.set('state', $('state-filter').value);
      const data = await request('jobs?' + query);
      if (version !== generation || activeView !== view) return;
      jobs = data.items; $('jobs-body').dataset.hasMore = String(data.hasMore); renderJobs(data.hasMore);
      if (selectedId && $('detail-dialog').open) await openDetail(selectedId, false);
    } else {
      const plans = await request('schedules');
      if (version !== generation || activeView !== view) return;
      renderSchedules(plans);
    }
    $('updated-at').textContent = '更新于 ' + date(new Date().toISOString());
  } catch (error) { report(error); }
  finally {
    if (version === generation) {
      busy = false; $('refresh').disabled = false;
      if (refreshAgain || activeView !== view) { refreshAgain = false; refresh(); }
    }
  }
}
function field(grid, label, value) { const item = element('div'); item.append(element('dt', label), element('dd', value ?? '—')); grid.append(item); }
async function openDetail(id, show = true) {
  if (!session) { showNotice('请先连接任务中心。'); return; }
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) {
    showNotice('请输入完整的 GUID 任务标识。'); return;
  }
  const detailVersion = ++detailRequest;
  const version = generation;
  const [job, attempts] = await Promise.all([request('jobs/' + encodeURIComponent(id)), request('jobs/' + encodeURIComponent(id) + '/attempts')]);
  if (version !== generation || detailVersion !== detailRequest || !show && selectedId !== id) return;
  selectedId = id; $('detail-title').textContent = job.handlerName;
  const content = $('detail-content'); content.replaceChildren(badge(job), element('div', job.jobId, 'detail-id'), progress(job));
  const grid = element('dl', null, 'detail-grid');
  field(grid, '队列 / 处理器版本', job.queue + ' / v' + job.handlerVersion); field(grid, '执行次数', job.attemptCount + ' / ' + job.maximumAttempts);
  field(grid, '创建时间', date(job.createdAtUtc)); field(grid, '最近更新', date(job.updatedAtUtc));
  field(grid, '下次可执行时间', date(job.nextRunAtUtc)); field(grid, '结束时间', date(job.finishedAtUtc)); content.append(grid);
  if (job.errorCode) { content.append(element('h3', '失败代码', 'detail-section'), element('pre', job.errorCode, 'detail-result')); }
  if (job.resultReference) { content.append(element('h3', '结果引用', 'detail-section'), element('pre', job.resultReference, 'detail-result')); }
  content.append(actions(job), element('h3', '执行历史 · ' + attempts.length + ' 次', 'detail-section'));
  for (const attempt of [...attempts].reverse()) {
    const item = element('article', null, 'attempt'), heading = element('div', null, 'attempt-heading');
    heading.append(element('strong', '第 ' + attempt.number + ' 次执行'), badge(attempt)); item.append(heading);
    item.append(element('p', date(attempt.startedAtUtc) + ' → ' + date(attempt.finishedAtUtc) + ' · ' + attempt.progress + '%'));
    if (attempt.progressMessage || attempt.errorCode) item.append(element('p', [attempt.progressMessage, attempt.errorCode].filter(Boolean).join(' · ')));
    content.append(item);
  }
  if (!attempts.length) content.append(element('p', '尚无执行记录。排队期间取消不会消耗执行次数。'));
  if (show && !$('detail-dialog').open) $('detail-dialog').showModal();
}
function confirmAction(job, action) {
  pendingAction = { id: job.jobId, action };
  $('confirm-title').textContent = action === 'cancel' ? '取消这个任务？' : '重新执行这个任务？';
  $('confirm-description').textContent = action === 'cancel' ? '运行中的任务会收到取消请求。已写入的结果或外部操作不会撤销。' : '将使用原参数重新排队，并保留执行历史。剩余可执行次数：' + (job.maximumAttempts - job.attemptCount) + '。';
  $('confirm-yes').textContent = action === 'cancel' ? '确认取消' : '确认重试'; $('confirm-dialog').showModal();
}
async function performAction() {
  if (!pendingAction || mutation) return;
  const action = pendingAction, version = generation; mutation = true; $('confirm-yes').disabled = true; showNotice('');
  try {
    await request('jobs/' + encodeURIComponent(action.id) + '/' + action.action, 'POST');
    $('confirm-dialog').close(); pendingAction = null;
    showNotice(action.action === 'cancel' ? '取消请求已提交，任务状态会在响应后更新。' : '任务已重新排队。');
    await refresh();
  } catch (error) { if (version === generation) { $('confirm-dialog').close(); report(error); } }
  finally {
    if (version === generation) {
      mutation = false; $('confirm-yes').disabled = false;
      document.querySelectorAll('[data-job-action]').forEach(button => button.disabled = false);
    }
  }
}
function switchView(next) {
  view = next; $('jobs-view').hidden = next !== 'jobs'; $('schedules-view').hidden = next !== 'schedules';
  for (const name of ['jobs', 'schedules']) {
    const selected = next === name; $('nav-' + name).classList.toggle('active', selected);
    if (selected) $('nav-' + name).setAttribute('aria-current', 'page'); else $('nav-' + name).removeAttribute('aria-current');
  }
  $('page-title').textContent = next === 'jobs' ? '持久任务' : '定时计划';
  $('page-description').textContent = next === 'jobs' ? '查看执行进度、处理结果与每一次重试。' : '查看 Coravel 注册计划的频率、时区与执行约束。';
  refresh();
}
$('nav-jobs').addEventListener('click', () => switchView('jobs'));
$('nav-schedules').addEventListener('click', () => switchView('schedules'));
$('refresh').addEventListener('click', refresh);
$('state-filter').addEventListener('change', refresh); $('take-filter').addEventListener('change', refresh);
$('filter-text').addEventListener('input', () => renderJobs($('jobs-body').dataset.hasMore === 'true'));
$('lookup-form').addEventListener('submit', event => { event.preventDefault(); openDetail($('lookup-id').value.trim()).catch(report); });
$('detail-close').addEventListener('click', () => $('detail-dialog').close());
$('detail-dialog').addEventListener('close', () => { selectedId = null; detailRequest++; });
$('auth-open').addEventListener('click', () => $('auth-dialog').showModal());
$('auth-close').addEventListener('click', () => { $('bearer-token').value = ''; $('auth-dialog').close(); });
$('auth-dialog').addEventListener('close', () => { $('bearer-token').value = ''; });
$('auth-form').addEventListener('submit', event => { event.preventDefault(); const bearer = $('bearer-token').value.trim().replace(/^Bearer\s+/i, ''); connect(bearer); });
$('disconnect').addEventListener('click', () => { resetConnection(); $('auth-dialog').close(); showNotice('已断开连接。'); });
$('confirm-no').addEventListener('click', () => { pendingAction = null; $('confirm-dialog').close(); });
$('confirm-yes').addEventListener('click', performAction);
setInterval(() => { if ($('auto-refresh').checked && !document.hidden) refresh(); }, 5000);
window.addEventListener('pagehide', resetConnection);
window.addEventListener('pageshow', event => { if (event.persisted) connect(); });
connect();
