'use strict';

const projectTitle = 'Информационная система для туристического агентства';
const state = { user: null, csrf: '', notice: '', routeVersion: 0 };
const main = document.querySelector('#main');
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money = value => new Intl.NumberFormat('ru-RU', {style:'currency',currency:'RUB'}).format(value / 100);
const date = value => value ? value.slice(0,10).split('-').reverse().join('.') : '—';
const dateTime = value => new Intl.DateTimeFormat('ru-RU', {dateStyle:'short',timeStyle:'short',timeZone:'Asia/Yekaterinburg'}).format(new Date(value));
const today = () => new Date(Date.now() + 5 * 3600000).toISOString().slice(0,10);
const fullName = client => [client.lastName,client.firstName,client.patronymic].filter(Boolean).join(' ');
const statusName = value => ({new:'Новая',in_progress:'В работе',awaiting_payment:'Ожидает оплаты',completed:'Завершена',cancelled:'Отменена'}[value] || value);
const available = tour => tour.isActive && tour.startsOn >= today();

class ApiError extends Error {
  constructor(status, data) { super(data.message || (status === 403 ? 'Недостаточно прав для этой операции.' : status === 404 ? 'Запись или операция не найдена.' : 'Не удалось выполнить запрос.')); this.status=status; this.errors=data.errors || {}; }
}

async function api(path, options = {}) {
  const method = options.method || 'GET';
  const headers = {'Accept':'application/json',...options.headers};
  if (options.body !== undefined) headers['Content-Type']='application/json';
  if (!['GET','HEAD'].includes(method)) headers['X-CSRF-TOKEN']=state.csrf;
  const response = await fetch(path, {credentials:'same-origin', ...options, headers, body: options.body === undefined ? undefined : JSON.stringify(options.body)});
  const text=await response.text(); let data={};
  try { data=text ? JSON.parse(text) : {}; } catch { data={message:'Сервер вернул неожиданный ответ.'}; }
  if (!response.ok) throw new ApiError(response.status,data);
  return data;
}
async function refreshCsrf() { state.csrf=(await api('/api/auth/csrf')).token; }
function go(route, notice='') { state.notice=notice; if (location.hash==='#'+route) navigate(); else location.hash=route; }

function account() {
  const nav=document.querySelector('#navigation'); const bar=document.querySelector('#account');
  nav.hidden=!state.user; bar.hidden=!state.user;
  if (!state.user) {bar.innerHTML='';return;}
  bar.innerHTML=`<span>${esc(state.user.fullName)} · ${state.user.role==='admin'?'Администратор':'Менеджер'}</span><button id="logout" class="logout" type="button">Выйти</button>`;
  document.querySelector('#logout').addEventListener('click',async()=>{
    try {await api('/api/auth/logout',{method:'POST'});state.user=null;await refreshCsrf();go('/');}
    catch(error){showGlobalError(error);}
  });
  const path=location.hash.slice(1).split('?')[0];
  for(const link of nav.querySelectorAll('a')) {
    const active=link.hash==='#/' ? path==='/' : path.startsWith(link.hash.slice(1));
    link.classList.toggle('active',active);
    if(active)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');
  }
}
function render(title, html, version=state.routeVersion) {
  if(version!==state.routeVersion)return false;
  document.title=`${title} · ${projectTitle}`;
  main.innerHTML=(state.notice?`<div class="message" role="status">${esc(state.notice)}</div>`:'')+html;
  state.notice='';account();return true;
}
function showGlobalError(error) {
  const div=document.createElement('div');div.className='form-errors';div.setAttribute('role','alert');div.textContent=error.message || 'Сервер недоступен. Проверьте, запущена ли программа.';main.prepend(div);
}
function heading(title, subtitle='', action='') {return `<div class="page-heading"><div><h1>${esc(title)}</h1>${subtitle?`<p>${esc(subtitle)}</p>`:''}</div>${action}</div>`;}
function linkButton(url, text, secondary=false) {return `<a class="button${secondary?' secondary':''}" href="#${url}">${esc(text)}</a>`;}
function field(name,label,value='',options={}) {
  const required=options.required?' required':'';
  const attrs=`id="f-${name}" name="${name}"${required}${options.max?` maxlength="${options.max}"`:''}`;
  const input=options.textarea?`<textarea ${attrs} rows="4">${esc(value)}</textarea>`:
    `<input ${attrs} type="${options.type||'text'}" value="${esc(value)}"${options.numeric?' inputmode="decimal"':''}${options.min?` min="${options.min}"`:''}${options.maxNumber?` max="${options.maxNumber}"`:''}>`;
  return `<div class="field${options.textarea?' field-wide':''}"><label for="f-${name}">${esc(label)}${options.required?' <span class="required">*</span>':''}</label>${input}<div class="field-error" data-error="${name}"></div></div>`;
}
function select(name,label,items,value='') {return `<div class="field"><label for="f-${name}">${esc(label)} <span class="required">*</span></label><select id="f-${name}" name="${name}" required><option value="">Выберите…</option>${items.map(item=>`<option value="${item.id}"${String(item.id)===String(value)?' selected':''}>${esc(item.name)}</option>`).join('')}</select><div class="field-error" data-error="${name}"></div></div>`;}
function formPage(title,fields,back,help='') {return `<a class="back-link" href="#${back}">← Назад</a>${heading(title,help)}<section class="panel form-panel"><form id="edit-form" novalidate><div class="form-errors" role="alert" hidden></div><div class="fields-grid">${fields}</div><div class="form-actions"><button type="submit" class="button">Сохранить</button>${linkButton(back,'Отмена',true)}<small>* Обязательные поля</small></div></form></section>`;}
function errors(form,error) {
  const box=form.querySelector('.form-errors');box.hidden=false;
  const messages=Object.values(error.errors||{}).flat();
  box.textContent=messages.length ? 'Проверьте заполнение полей.' : error.message;
  if(messages.length && error.errors[''])box.textContent=error.errors[''].join(' ');
  for(const [name,messages] of Object.entries(error.errors||{})) {
    const target=Array.from(form.querySelectorAll('[data-error]')).find(el=>el.dataset.error===name);
    if(target)target.textContent=messages.join(' ');
    const input=form.elements.namedItem(name);if(input && input.setAttribute)input.setAttribute('aria-invalid','true');
  }
  box.scrollIntoView({block:'nearest'});
}
function bindForm(handler) {
  const form=document.querySelector('#edit-form');
  form.addEventListener('submit',async event=>{
    event.preventDefault();const button=form.querySelector('button[type="submit"]');button.disabled=true;
    form.querySelector('.form-errors').hidden=true;
    for(const el of form.querySelectorAll('[data-error]'))el.textContent='';
    for(const el of form.querySelectorAll('[aria-invalid]'))el.removeAttribute('aria-invalid');
    try {await handler(new FormData(form));}
    catch(error){if(error.status===401 && state.user){state.user=null;await refreshCsrf();login();}else errors(form,error);}
    finally {button.disabled=false;}
  });
}
function bookingTable(items) {
  return `<div class="table-wrap"><table><thead><tr><th>Заявка / клиент</th><th>Тур</th><th>Состояние</th><th>Стоимость</th><th></th></tr></thead><tbody>${items.length?items.map(item=>`<tr><td>№ ${item.id}<small>${esc(item.clientName)}</small></td><td>${esc(item.tourTitle)}<small>${date(item.startsOn)} — ${date(item.endsOn)}</small></td><td>${esc(statusName(item.status))}</td><td class="numeric">${money(item.totalPriceKopecks)}</td><td><a href="#/bookings/${item.id}">Открыть</a></td></tr>`).join(''):'<tr><td colspan="5">Заявок пока нет.</td></tr>'}</tbody></table></div>`;
}

function login() {
  render('Вход',`<div class="login-main"><section class="panel login-card"><h1>Вход в систему</h1><p>Введите логин и пароль сотрудника.</p><form id="edit-form" novalidate><div class="form-errors" role="alert" hidden></div><div class="field"><label for="username">Логин <span class="required">*</span></label><input id="username" name="username" autocomplete="username" required maxlength="100"></div><div class="field"><label for="password">Пароль <span class="required">*</span></label><input id="password" name="password" type="password" autocomplete="current-password" required></div><button class="button" type="submit">Войти</button></form><p class="help">Если не удаётся войти, обратитесь к администратору.</p></section></div>`);
  bindForm(async data=>{state.user=await api('/api/auth/login',{method:'POST',body:{username:data.get('username'),password:data.get('password')}});await refreshCsrf();go('/');});
}
async function dashboard(version) {
  const data=await api('/api/dashboard');
  render('Главная',heading('Главная','',linkButton('/bookings/new','Новая заявка'))+
    `<section class="stats"><div class="stat"><span>Клиенты</span><strong>${data.clientCount}</strong></div><div class="stat"><span>Активные туры</span><strong>${data.tourCount}</strong></div><div class="stat"><span>Созданные заявки</span><strong>${data.requestCount}</strong></div></section><section class="panel"><div class="panel-heading"><h2>Последние заявки</h2><a href="#/bookings">Все заявки</a></div>${bookingTable(data.latest)}</section>`,version);
}
async function clientsPage(query,version) {
  const q=query.get('q')||'';const items=await api('/api/clients?q='+encodeURIComponent(q));
  if(!render('Клиенты',heading('Клиенты','Контакты и история обращений.',linkButton('/clients/new','Новый клиент'))+
    `<section class="panel"><form id="search-form" class="filters"><label for="search">Поиск</label><input id="search" name="q" value="${esc(q)}" placeholder="Фамилия или телефон"><button class="button secondary">Найти</button></form><div class="table-wrap"><table><thead><tr><th>Клиент</th><th>Телефон</th><th>Email</th><th></th></tr></thead><tbody>${items.length?items.map(item=>`<tr><td>${esc(fullName(item))}</td><td>${esc(item.phone)}</td><td>${esc(item.email)||'—'}</td><td><a href="#/clients/${item.id}">Открыть</a></td></tr>`).join(''):'<tr><td colspan="4">Клиенты не найдены.</td></tr>'}</tbody></table></div></section>`,version))return;
  document.querySelector('#search-form').addEventListener('submit',event=>{event.preventDefault();go('/clients?q='+encodeURIComponent(new FormData(event.target).get('q')));});
}
async function clientDetail(id,version) {
  const data=await api('/api/clients/'+id);const c=data.client;
  render('Клиент',`<a class="back-link" href="#/clients">← Клиенты</a>`+heading(fullName(c),c.phone,`<div class="actions">${linkButton('/clients/'+id+'/edit','Изменить',true)}${linkButton('/bookings/new?client='+id,'Оформить заявку')}</div>`)+
    `<section class="panel padded"><h2>Контактные данные</h2><dl class="details"><dt>Телефон</dt><dd>${esc(c.phone)}</dd><dt>Email</dt><dd>${esc(c.email)||'—'}</dd><dt>Комментарий</dt><dd class="prose">${esc(c.comments)||'—'}</dd></dl></section><section class="panel"><div class="panel-heading"><h2>История обращений</h2></div>${bookingTable(data.bookings)}</section>`,version);
}
async function clientForm(id,version) {
  const c=id?(await api('/api/clients/'+id)).client:{};
  const fields=field('lastName','Фамилия',c.lastName,{required:true,max:100})+field('firstName','Имя',c.firstName,{required:true,max:100})+
    field('patronymic','Отчество',c.patronymic,{max:100})+field('phone','Телефон',c.phone,{required:true,type:'tel',max:25})+
    field('email','Email',c.email,{type:'email',max:254})+field('comments','Комментарий',c.comments,{textarea:true,max:2000});
  if(!render(id?'Изменить клиента':'Новый клиент',formPage(id?'Изменить клиента':'Новый клиент',fields,id?'/clients/'+id:'/clients','Телефон указывается с кодом страны.'),version))return;
  bindForm(async form=>{const saved=await api('/api/clients'+(id?'/'+id:''),{method:id?'PUT':'POST',body:Object.fromEntries(form)});go('/clients/'+saved.id,'Карточка клиента сохранена.');});
}
async function toursPage(query,version) {
  const all=await api('/api/tours');const archive=query.get('archive')==='1';const items=all.filter(t=>archive?!available(t):available(t));
  render('Туры',heading('Каталог туров','Направления и предложения.',state.user.role==='admin'?linkButton('/tours/new','Новое предложение'):'')+
    `<nav class="tabs"><a href="#/tours"${!archive?' aria-current="page"':''}>Активные предложения</a><a href="#/tours?archive=1"${archive?' aria-current="page"':''}>Архив</a></nav><section class="panel"><div class="table-wrap"><table><thead><tr><th>Тур</th><th>Направление / отель</th><th>Даты</th><th>Цена</th><th></th></tr></thead><tbody>${items.length?items.map(t=>`<tr><td>${esc(t.title)}</td><td>${esc(t.country)}, ${esc(t.city)}<small>${esc(t.hotel)}</small></td><td>${date(t.startsOn)} — ${date(t.endsOn)}</td><td class="numeric">${money(t.priceKopecks)}</td><td><a href="#/tours/${t.id}">Подробнее</a></td></tr>`).join(''):'<tr><td colspan="5">Предложений пока нет.</td></tr>'}</tbody></table></div></section>`,version);
}
async function tourDetail(id,version) {
  const t=await api('/api/tours/'+id);
  render('Тур',`<a class="back-link" href="#/tours">← Каталог туров</a>`+heading(t.title,`${date(t.startsOn)} — ${date(t.endsOn)}`,`<div class="actions">${state.user.role==='admin'?linkButton('/tours/'+id+'/edit','Изменить',true):''}${available(t)?linkButton('/bookings/new?tour='+id,'Оформить заявку'):''}</div>`)+
    `<section class="panel padded"><h2>Условия предложения</h2><dl class="details"><dt>Направление</dt><dd>${esc(t.country)}, ${esc(t.city)}</dd><dt>Отель</dt><dd>${esc(t.hotel)}</dd><dt>Туроператор</dt><dd>${esc(t.operator)}</dd><dt>Цена предложения</dt><dd>${money(t.priceKopecks)}</dd><dt>Доступность</dt><dd>${available(t)?'Доступен для новых заявок':'Архивное предложение'}</dd><dt>Услуги</dt><dd class="prose">${esc(t.services)||'—'}</dd></dl></section>`,version);
}
async function tourForm(id,version) {
  if(state.user.role!=='admin')throw new ApiError(403,{});
  const [lookups,t]=await Promise.all([api('/api/lookups'),id?api('/api/tours/'+id):Promise.resolve({isActive:true})]);
  const fields=field('title','Название тура',t.title,{required:true,max:200})+select('hotelId','Отель / направление',lookups.hotels,t.hotelId)+
    select('operatorId','Туроператор',lookups.operators,t.operatorId)+field('startsOn','Начало поездки',t.startsOn,{type:'date',required:true})+
    field('endsOn','Окончание поездки',t.endsOn,{type:'date',required:true})+field('price','Цена предложения, ₽',t.priceKopecks===undefined?'':(t.priceKopecks/100).toFixed(2),{required:true,numeric:true})+
    field('services','Перечень услуг',t.services,{textarea:true,max:4000})+`<div class="field field-wide"><label class="checkbox-label"><input name="isActive" type="checkbox"${t.isActive?' checked':''}>Активное предложение</label></div>`;
  if(!render(id?'Изменить тур':'Новый тур',formPage(id?'Изменить тур':'Новый тур',fields,id?'/tours/'+id:'/tours','Выберите отель, даты поездки и укажите стоимость.'),version))return;
  bindForm(async form=>{const data=Object.fromEntries(form);data.hotelId=Number(data.hotelId);data.operatorId=Number(data.operatorId);data.isActive=form.has('isActive');const saved=await api('/api/tours'+(id?'/'+id:''),{method:id?'PUT':'POST',body:data});go('/tours/'+saved.id,'Предложение сохранено.');});
}
async function bookingsPage(version) {
  const items=await api('/api/bookings');
  render('Заявки',heading('Заявки','Заявки клиентов.',linkButton('/bookings/new','Новая заявка'))+`<section class="panel">${bookingTable(items)}</section>`,version);
}
async function bookingDetail(id,version) {
  const b=await api('/api/bookings/'+id);
  render('Заявка № '+id,`<a class="back-link" href="#/bookings">← Все заявки</a>`+heading('Заявка № '+id,b.clientName)+
    `<section class="panel padded"><h2>Данные заявки</h2><dl class="details"><dt>Клиент</dt><dd><a href="#/clients/${b.clientId}">${esc(b.clientName)}</a></dd><dt>Телефон</dt><dd>${esc(b.phone)}</dd><dt>Тур</dt><dd><a href="#/tours/${b.tourId}">${esc(b.tourTitle)}</a></dd><dt>Направление / отель</dt><dd>${esc(b.destination)} / ${esc(b.hotel)}</dd><dt>Даты поездки</dt><dd>${date(b.startsOn)} — ${date(b.endsOn)}</dd><dt>Количество туристов</dt><dd>${b.travelersCount}</dd><dt>Ответственный</dt><dd>${esc(b.managerName)}</dd><dt>Полная стоимость</dt><dd>${money(b.totalPriceKopecks)}</dd><dt>Статус</dt><dd>${esc(statusName(b.status))}</dd><dt>Создана</dt><dd>${dateTime(b.createdAt)}</dd></dl></section>`,version);
}
async function bookingForm(query,version) {
  const [clients,tours,lookups]=await Promise.all([api('/api/clients'),api('/api/tours?activeOnly=true'),api('/api/lookups')]);
  const initialTour=tours.find(t=>String(t.id)===query.get('tour'));
  const fields=select('clientId','Клиент',clients.map(c=>({id:c.id,name:fullName(c)+' · '+c.phone})),query.get('client'))+
    select('tourId','Тур',tours.map(t=>({id:t.id,name:t.title+' · '+money(t.priceKopecks)})),query.get('tour'))+
    select('managerId','Ответственный менеджер',lookups.managers,state.user.id)+field('travelersCount','Количество туристов',1,{type:'number',required:true,min:1,maxNumber:100})+
    field('totalPrice','Итоговая стоимость всей заявки, ₽',initialTour?(initialTour.priceKopecks/100).toFixed(2):'',{required:true,numeric:true});
  if(!render('Новая заявка',formPage('Новая заявка',fields,'/bookings','Укажите полную стоимость заявки. Цена не умножается на число туристов автоматически.')+
    `<p>${linkButton('/clients/new','Добавить клиента',true)}</p>`,version))return;
  bindForm(async form=>{const data=Object.fromEntries(form);for(const key of ['clientId','tourId','managerId','travelersCount'])data[key]=Number(data[key]);const saved=await api('/api/bookings',{method:'POST',body:data});go('/bookings/'+saved.id,'Заявка создана.');});
}
function paymentsPage(version) {
  render('Платежи',heading('Платежи'),version);
}
async function navigate() {
  const version=++state.routeVersion;
  if(!state.user){login();return;}
  const [path,search='']=(location.hash.slice(1)||'/').split('?');const query=new URLSearchParams(search);
  account();main.setAttribute('aria-busy','true');
  try {
    let match;
    if(path==='/')await dashboard(version);
    else if(path==='/clients')await clientsPage(query,version);
    else if(path==='/clients/new')await clientForm(null,version);
    else if((match=path.match(/^\/clients\/(\d+)\/edit$/)))await clientForm(match[1],version);
    else if((match=path.match(/^\/clients\/(\d+)$/)))await clientDetail(match[1],version);
    else if(path==='/tours')await toursPage(query,version);
    else if(path==='/tours/new')await tourForm(null,version);
    else if((match=path.match(/^\/tours\/(\d+)\/edit$/)))await tourForm(match[1],version);
    else if((match=path.match(/^\/tours\/(\d+)$/)))await tourDetail(match[1],version);
    else if(path==='/bookings')await bookingsPage(version);
    else if(path==='/bookings/new')await bookingForm(query,version);
    else if((match=path.match(/^\/bookings\/(\d+)$/)))await bookingDetail(match[1],version);
    else if(path==='/payments')paymentsPage(version);
    else render('Страница не найдена',heading('Страница не найдена')+linkButton('/','На главную'),version);
  }catch(error){
    if(version!==state.routeVersion)return;
    if(error.status===401){state.user=null;await refreshCsrf();login();}
    else render('Ошибка',heading(error.status===403?'Доступ запрещён':'Не удалось открыть страницу')+`<div class="form-errors" role="alert">${esc(error.message || 'Проверьте, запущен ли сервер.')}</div>`+linkButton('/','На главную'),version);
  }finally{if(version===state.routeVersion)main.removeAttribute('aria-busy');}
}
window.addEventListener('hashchange',navigate);
(async()=>{
  try {
    const response=await fetch('/api/auth/me',{credentials:'same-origin'});
    if(response.ok)state.user=await response.json();
    await refreshCsrf();await navigate();
  } catch(error) {main.innerHTML='<h1>Не удалось подключиться к серверу</h1><p>Запустите программу и обновите страницу.</p>';}
})();
