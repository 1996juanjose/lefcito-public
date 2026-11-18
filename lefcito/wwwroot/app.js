'use strict';

const byId = id => document.getElementById(id);
const create = byId('create');
const score = byId('score');
const select = byId('tournaments');
const teams = byId('teamFields');
const rules = byId('ruleFields');
const events = byId('events');
let current = null;
let selection = 0;
let editing = null;
let catalog = [];
let savedGroups = [];

function node(tag, text, className) {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    if (className) element.className = className;
    return element;
}
function option(value, label = value) {
    const element = node('option', label);
    element.value = value;
    return element;
}
function field(label, input) {
    const element = node('label', label);
    element.append(input);
    return element;
}
function input(value, attributes = {}) {
    const element = node('input');
    Object.assign(element, { required: true, ...attributes, value });
    return element;
}
function button(title, action) {
    const element = node('button', title, 'quiet');
    element.type = 'button';
    element.addEventListener('click', action);
    return element;
}
async function api(suffix = '', method = 'GET', body) {
    return request('/api/torneos' + suffix, method, body);
}
async function request(path, method = 'GET', body) {
    const response = await fetch(path, {
        method,
        ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
    });
    if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        throw new Error(problem.detail || 'No se pudo completar la solicitud (' + response.status + ').');
    }
    return response.status === 204 ? null : response.json();
}
function showError(message = '') {
    byId('error').hidden = !message;
    byId('error').textContent = message;
}
function render(view) {
    current = view;
    byId('welcome').hidden = Boolean(view);
    byId('tournament').hidden = !view;
    if (!view) return;
    byId('title').textContent = view.name;
    const direct = view.teams.length === 2;
    const tournamentMode = view.mode === 'torneo' || !direct;
    byId('title').previousElementSibling.textContent = tournamentMode ? 'Left 4 Dead 2 · Torneo' : 'Left 4 Dead 2 · Partida';
    const final = view.matches.find(match => match.id === 'final');
    byId('champion').textContent = tournamentMode && final.winner ? 'Campeón · ' + final.winner.name : '';
    byId('addEncounter').hidden = !direct || view.mode != null;
    const bracket = byId('bracket');
    bracket.classList.toggle('direct', direct);
    bracket.replaceChildren();
    if (direct) {
        bracket.append(...view.matches.map(matchCard));
    } else {
        const semis = node('div');
        semis.append(...view.matches.filter(match => match.id !== 'final').map(matchCard));
        const last = node('div');
        last.append(matchCard(final));
        bracket.append(semis, last);
    }
    byId('standings').replaceChildren(...view.standings.map(player => {
        const row = node('tr');
        row.append(...[player.name, player.team, player.wins, player.losses, player.resultPoints,
            player.eventPoints, player.total].map(value => node('td', String(value))));
        return row;
    }));
    renderGroupRows('groupScores', view.groupStandings);
    byId('rosters').replaceChildren(...view.teams.map(team => {
        const card = node('article', undefined, 'card');
        const list = node('ul', undefined, 'roster');
        list.append(...team.players.map(player => node('li', player)));
        card.append(node('h3', team.name), list);
        return card;
    }));
}
function renderGroupRows(id, groups) {
    byId(id).replaceChildren(...groups.map(group => {
        const row = node('tr');
        row.append(...[group.name, group.wins, group.losses, group.points].map(value => node('td', String(value))));
        return row;
    }));
}
byId('groupRankingButton').addEventListener('click', async () => {
    showError();
    try {
        renderGroupRows('groupRankingRows', await request('/api/grupos'));
        byId('groupRanking').showModal();
    } catch (error) { showError(error.message); }
});
function matchCard(match) {
    const card = node('article', undefined, 'series');
    const format = match.format === 0 ? 'Marcador libre' : 'Al mejor de ' + match.format + ' · ' + match.target + ' para ganar';
    const status = match.winner ? 'Terminado' : match.score ? 'En curso' : 'Pendiente';
    card.append(node('h3', match.stage), node('small', format + ' · ' + status));
    for (const side of ['a', 'b']) {
        const line = node('div', undefined, 'teamLine');
        const team = match[side];
        if (team && match.winner?.name === team.name) line.classList.add('winner');
        line.append(node('strong', team?.name || 'Por definir'), node('span', String(match.score?.[side] ?? '–')));
        card.append(line);
    }
    if (match.isLocked) card.append(node('p', 'Marcador cerrado. Edición bloqueada.'));
    if (match.a && match.b && !match.isLocked) card.append(button(match.score ? 'Editar marcador' : 'Registrar marcador', () => openScore(match)));
    return card;
}
async function loadTournament(id) {
    const request = ++selection;
    showError();
    try {
        const view = id ? await api('/' + id) : null;
        if (selection === request) render(view);
    } catch (error) {
        if (selection === request) {
            render(null);
            showError(error.message);
        }
    }
}
async function refresh(id) {
    const items = await api();
    select.replaceChildren(option('', 'Seleccionar…'), ...items.map(item => option(item.id, item.name)));
    select.value = id || items[0]?.id || '';
    await loadTournament(select.value);
}
select.addEventListener('change', () => loadTournament(select.value));
for (const picker of document.querySelectorAll('.format')) {
    picker.append(...[1, 3, 5, 7, 9].map(n => option(n, 'Al mejor de ' + n)));
    picker.value = picker.name === 'finalBestOf' ? '5' : '3';
}
function updateTeams() {
    const isMatch = byId('mode').value === 'partida';
    const count = isMatch ? 2 : Number(byId('teamCount').value);
    byId('teamCountField').hidden = isMatch;
    byId('semiFormatField').hidden = isMatch || count === 2;
    byId('finalFormatField').hidden = isMatch;
    while (teams.children.length > count) teams.lastChild.remove();
    while (teams.children.length < count) {
        const card = node('div', undefined, 'card');
        const roster = node('fieldset', undefined, 'player-choices');
        roster.append(node('legend', 'Jugadores'));
        const group = node('select', undefined, 'saved-group');
        const name = input('', { maxLength: 40 });
        const warning = node('p', '', 'group-warning');
        warning.setAttribute('role', 'status');
        group.addEventListener('change', () => selectGroup(card, group, name, warning));
        name.addEventListener('input', () => { group.value = ''; warning.textContent = ''; });
        card.append(field('Grupo guardado', group), field('Equipo ' + (teams.children.length + 1), name), warning, roster);
        teams.append(card);
    }
    updateGroupChoices();
    updatePlayerChoices();
    byId('formatNote').textContent = isMatch ? 'Dos equipos, un solo enfrentamiento y marcador libre.' : ({
        2: 'Los dos equipos juegan un solo enfrentamiento. El formato de la final define la cantidad de rondas.',
        3: 'Los equipos 1 y 2 juegan la semifinal. El equipo 3 pasa directo a la final.',
        4: 'Semifinales: equipo 1 contra 2 y equipo 3 contra 4.'
    })[count];
}
function updateGroupChoices() {
    for (const picker of teams.querySelectorAll('.saved-group')) {
        const selected = picker.value;
        picker.replaceChildren(option('', 'Armar grupo'), ...savedGroups.map(group => option(group.name)));
        picker.value = savedGroups.some(group => group.name === selected) ? selected : '';
    }
}
function selectGroup(card, picker, name, warning) {
    warning.textContent = '';
    const group = savedGroups.find(item => item.name === picker.value);
    if (!group) return;
    const choices = [...card.querySelectorAll('input[type=checkbox]')];
    const members = choices.filter(choice => group.players.includes(choice.value));
    if (members.length !== group.players.length || members.some(choice => choice.disabled)) {
        warning.textContent = 'Algún integrante no está disponible. Revisa los jugadores en los demás equipos.';
        picker.value = savedGroups.some(item => item.name === name.value) ? name.value : '';
        return;
    }
    name.value = group.name;
    for (const choice of choices) choice.checked = members.includes(choice);
    updateAvailability();
}
function updatePlayerChoices() {
    for (const roster of teams.querySelectorAll('fieldset')) {
        const checked = new Set([...roster.querySelectorAll('input:checked')].map(input => input.value));
        roster.replaceChildren(node('legend', 'Jugadores'), ...catalog.map(player => {
            const choice = input(player.nickname, { type: 'checkbox', required: false, checked: checked.has(player.nickname) });
            choice.addEventListener('change', updateAvailability);
            const label = node('label', undefined, 'player-choice');
            label.append(choice, node('span', player.nickname));
            return label;
        }));
    }
    updateAvailability();
}
function updateAvailability() {
    const choices = [...teams.querySelectorAll('input[type=checkbox]')];
    const assigned = new Set(choices.filter(choice => choice.checked).map(choice => choice.value));
    for (const choice of choices) choice.disabled = !choice.checked && assigned.has(choice.value);
}
async function fetchCatalog() {
    catalog = await request('/api/jugadores');
    byId('registeredPlayers').replaceChildren(...catalog.map(player => node('li', player.nickname)));
}
byId('playersButton').addEventListener('click', async () => {
    showError();
    try {
        await fetchCatalog();
        byId('playerDirectory').showModal();
    } catch (error) { showError(error.message); }
});
byId('rankingButton').addEventListener('click', async () => {
    showError();
    try {
        const ranking = await request('/api/ranking');
        byId('rankingRows').replaceChildren(...ranking.map(player => {
            const row = node('tr');
            row.append(...[player.nickname, player.wins, player.losses, player.resultPoints, player.eventPoints, player.total]
                .map(value => node('td', String(value))));
            return row;
        }));
        byId('globalRanking').showModal();
    } catch (error) { showError(error.message); }
});
function addRule(name = '', points = '', container = rules) {
    const row = node('div', undefined, 'entry');
    row.append(field('Evento', input(name, { maxLength: 60 })),
        field('Puntos por evento', input(points, { type: 'number', step: 'any', min: -10000, max: 10000 })),
        button('Quitar', () => row.remove()));
    container.append(row);
}
byId('teamCount').addEventListener('change', updateTeams);
byId('mode').addEventListener('change', updateTeams);
byId('addRule').addEventListener('click', () => addRule());
updateTeams();
addRule('Instakill Charger', 1);
addRule('Insta recibida', -1);
addRule('Asistencia', 0.5);
addRule('Aura', 0.25);
byId('new').addEventListener('click', async () => {
    showError();
    try {
        const [, groups] = await Promise.all([fetchCatalog(), request('/api/grupos')]);
        savedGroups = groups;
        updateGroupChoices();
        updatePlayerChoices();
        byId('setup').showModal();
    } catch (error) { showError(error.message); }
});
for (const close of document.querySelectorAll('[data-close]')) {
    close.addEventListener('click', () => close.closest('dialog').close());
}
function onSubmit(form, work) {
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const status = form.querySelector('.form-error');
        const submit = form.querySelector('button:not([type])');
        status.textContent = '';
        submit.disabled = true;
        try { await work(); }
        catch (error) { status.textContent = error.message; }
        finally { submit.disabled = false; }
    });
}
const register = byId('registerPlayer');
onSubmit(register, async () => {
    const { nickname } = await request('/api/jugadores', 'POST', { nickname: new FormData(register).get('nickname') });
    register.reset();
    register.querySelector('.form-error').textContent = 'Jugador registrado: ' + nickname;
    await fetchCatalog();
});
onSubmit(create, async () => {
    const data = new FormData(create);
    const result = await api('', 'POST', {
        name: data.get('name'),
        mode: data.get('mode'),
        semiBestOf: Number(data.get('semiBestOf')),
        finalBestOf: Number(data.get('finalBestOf')),
        teams: [...teams.children].map(card => ({
            name: card.querySelector('input').value,
            players: [...card.querySelectorAll('input:checked')].map(choice => choice.value)
        })),
        scoring: scoringPayload(data, rules)
    });
    byId('setup').close();
    await refresh(result.id).catch(error => showError(error.message));
});
function scoringPayload(data, container) {
    return {
        win: Number(data.get('win')), loss: Number(data.get('loss')),
        events: [...container.children].map(row => {
            const [name, points] = row.querySelectorAll('input');
            return { name: name.value.trim(), points: Number(points.value) };
        })
    };
}
const activeRules = byId('activeRuleFields');
const rulesForm = byId('updateRules');
let ruleEdit = null;
byId('editRules').addEventListener('click', () => {
    const { id, revision, scoring } = current;
    ruleEdit = { id, revision };
    rulesForm.elements.win.value = scoring.win;
    rulesForm.elements.loss.value = scoring.loss;
    rulesForm.querySelector('.form-error').textContent = '';
    activeRules.replaceChildren();
    for (const event of scoring.events) addRule(event.name, event.points, activeRules);
    byId('scoring').showModal();
});
byId('addActiveRule').addEventListener('click', () => addRule('', '', activeRules));
onSubmit(rulesForm, async () => {
    const { id, revision } = ruleEdit;
    await api('/' + id + '/reglas', 'PUT', { revision, scoring: scoringPayload(new FormData(rulesForm), activeRules) });
    byId('scoring').close();
    await loadTournament(id);
});
byId('addEncounter').addEventListener('click', async () => {
    const control = byId('addEncounter');
    const { id, revision } = current;
    control.disabled = true;
    showError();
    try {
        await api('/' + id + '/cruces', 'POST', { revision });
        if (current?.id === id) await loadTournament(id);
    } catch (error) { showError(error.message); }
    finally { control.disabled = false; }
});
const encounterFormat = byId('encounterFormat');
encounterFormat.append(option(0, 'Marcador libre'), ...[1, 3, 5, 7, 9].map(n => option(n, 'Al mejor de ' + n)));
encounterFormat.addEventListener('change', showScoreFormat);
score.elements.closed.addEventListener('change', showScoreFormat);
function showScoreFormat() {
    const format = Number(encounterFormat.value);
    const target = Math.floor(format / 2) + 1;
    byId('target').textContent = format === 0
        ? 'Los puntos se actualizan al guardar. Cierra el enfrentamiento cuando el marcador sea definitivo.'
        : 'Gana el primero en llegar a ' + target + '.';
    byId('closeEncounter').hidden = format !== 0;
    byId('saveScore').textContent = format === 0
        ? score.elements.closed.checked ? 'Cerrar y bloquear marcador' : 'Guardar y actualizar puntos'
        : 'Guardar marcador';
    for (const points of byId('scoreFields').querySelectorAll('input')) points.max = format === 0 ? 1000000 : target;
}
function openScore(match) {
    editing = { id: current.id, revision: current.revision, match, rules: current.scoring.events };
    score.querySelector('.form-error').textContent = '';
    byId('matchTitle').textContent = match.stage;
    const legacySingleRound = current.teams.length === 2 && match.format === 1 && match.score?.format == null;
    byId('encounterFormatField').hidden = current.mode === 'partida';
    encounterFormat.value = current.mode === 'partida' || legacySingleRound ? '0' : String(match.format);
    score.elements.closed.checked = false;
    byId('scoreFields').replaceChildren(...['a', 'b'].map(side => field(match[side].name,
        input(match.score?.[side] ?? 0, { type: 'number', min: 0, name: side }))));
    showScoreFormat();
    events.replaceChildren();
    byId('addEvent').hidden = !editing.rules.length;
    for (const entry of match.score?.events ?? []) addEvent(entry);
    byId('editor').showModal();
}
function addEvent(entry = {}) {
    const row = node('div', undefined, 'entry');
    const player = node('select');
    player.append(...[...editing.match.a.players, ...editing.match.b.players].map(name => option(name)));
    const rule = node('select');
    rule.append(...editing.rules.map(item => option(item.name, item.name + ' (' + item.points + ')')));
    if (entry.player) player.value = entry.player;
    if (entry.rule) rule.value = entry.rule;
    row.append(field('Jugador', player), field('Evento', rule),
        field('Cantidad', input(entry.count ?? 1, { type: 'number', min: 1, max: 1000 })), button('Quitar', () => row.remove()));
    events.append(row);
}
byId('addEvent').addEventListener('click', () => addEvent());
onSubmit(score, async () => {
    const { id, revision, match } = editing;
    const data = new FormData(score);
    await api('/' + id + '/cruces/' + match.id, 'PUT', {
        revision,
        score: {
            a: Number(data.get('a')), b: Number(data.get('b')),
            format: Number(data.get('format')), closed: data.get('closed') === 'on',
            events: [...events.children].map(row => {
                const [player, rule] = row.querySelectorAll('select');
                return { player: player.value, rule: rule.value, count: Number(row.querySelector('input').value) };
            })
        }
    });
    byId('editor').close();
    await loadTournament(id);
});
refresh().catch(error => showError(error.message));
