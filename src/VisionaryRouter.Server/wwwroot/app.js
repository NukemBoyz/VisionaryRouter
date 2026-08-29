/*
   Visionary Router - browser client.

   Plain ES modules-free JavaScript on purpose. There is no build step, nothing to npm
   install, and every file here is the file that runs. For a control surface this small,
   that is worth more than a framework.

   State comes from the server over Server-Sent Events. This file never assumes a route
   succeeded because a button was pressed - it renders what the decoders report.
*/

'use strict';

const state = {
    devices: [],
    selectedSource: null,
    selectedDestinations: new Set()
};

// --- element lookups --------------------------------------------------------

const el = {
    connection: document.getElementById('connection'),
    sources: document.getElementById('sources'),
    destinations: document.getElementById('destinations'),
    routeBtn: document.getElementById('route-btn'),
    routeSummary: document.getElementById('route-summary'),
    routeResults: document.getElementById('route-results'),
    selectAll: document.getElementById('select-all'),
    selectNone: document.getElementById('select-none'),
    addForm: document.getElementById('add-form'),
    encoderTable: document.getElementById('encoder-table'),
    decoderTable: document.getElementById('decoder-table'),
    unknownTable: document.getElementById('unknown-table'),
    unclassifiedPanel: document.getElementById('unclassified-panel'),
    statusGrid: document.getElementById('status-grid'),
    log: document.getElementById('log')
};

// --- helpers ----------------------------------------------------------------

function h(tag, attrs = {}, ...children) {
    const node = document.createElement(tag);

    for (const [key, value] of Object.entries(attrs)) {
        if (value === null || value === undefined || value === false) continue;

        if (key === 'class') node.className = value;
        else if (key === 'text') node.textContent = value;
        else if (key.startsWith('on')) node.addEventListener(key.slice(2).toLowerCase(), value);
        else node.setAttribute(key, value === true ? '' : value);
    }

    for (const child of children.flat()) {
        if (child === null || child === undefined || child === false) continue;
        node.append(child.nodeType ? child : document.createTextNode(String(child)));
    }

    return node;
}

function log(message, level) {
    const line = h('div', { class: 'log-line' },
        h('span', { class: 'log-time', text: new Date().toLocaleTimeString() }),
        h('span', { class: level ? 'log-' + level : '', text: message }));

    el.log.prepend(line);

    while (el.log.childElementCount > 300) {
        el.log.lastElementChild.remove();
    }
}

async function api(method, path, body) {
    const response = await fetch(path, {
        method,
        headers: body ? { 'Content-Type': 'application/json' } : undefined,
        body: body ? JSON.stringify(body) : undefined
    });

    const text = await response.text();
    const payload = text ? JSON.parse(text) : null;

    if (!response.ok) {
        throw new Error(payload && payload.error ? payload.error : `${method} ${path} failed (${response.status})`);
    }

    return payload;
}

// How many slots the CSS palette defines. Beyond this, colours repeat - a system with more
// than eight simultaneous sources gets duplicates rather than running out.
const PALETTE_SIZE = 8;

/*
   Resolves a colour slot to the CSS variable holding it.

   The server decides which slot a source occupies, so the assignment survives a reload and
   stays put when devices are renamed or added. The browser only turns a number into a colour.
*/
function sourceColour(colorIndex) {
    return colorIndex === null || colorIndex === undefined
        ? 'var(--src-none)'
        : `var(--src-${colorIndex % PALETTE_SIZE})`;
}

const encoders = () => state.devices.filter(d => d.type === 'Encoder');
const decoders = () => state.devices.filter(d => d.type === 'Decoder');
const unclassified = () => state.devices.filter(d => d.type === 'Unknown');
const byId = id => state.devices.find(d => d.id === id);

// --- routing view -----------------------------------------------------------

function renderRouting() {
    const sources = encoders();
    const destinations = decoders();

    el.sources.replaceChildren(...(sources.length
        ? sources.map(renderSourceCard)
        : [h('div', { class: 'empty', text: 'No encoders yet. Add one on the Devices tab.' })]));

    el.destinations.replaceChildren(...(destinations.length
        ? destinations.map(renderDestinationCard)
        : [h('div', { class: 'empty', text: 'No decoders yet. Add one on the Devices tab.' })]));

    renderRouteBar();
}

function renderSourceCard(device) {
    const selected = state.selectedSource === device.id;

    return h('button', {
        class: `card card-source${selected ? ' is-selected' : ''}${device.onlineState === 'Offline' ? ' is-offline' : ''}`,
        style: `--card-colour: ${sourceColour(device.colorIndex)}`,
        onclick: () => {
            state.selectedSource = selected ? null : device.id;
            renderRouting();
        }
    },
        h('span', { class: `state-dot state-${device.onlineState}` }),
        h('span', { class: 'card-body' },
            h('div', { class: 'card-name', text: device.name }),
            h('div', { class: 'card-sub', text: `${device.ipAddress}${device.model ? '  ·  ' + device.model : ''}` })));
}

function renderDestinationCard(device) {
    const selected = state.selectedDestinations.has(device.id);

    // Show what the decoder says it is following, resolved to a friendly name when we know
    // the encoder. An unknown IP is shown raw rather than hidden - it means something outside
    // this application is feeding that display, which is worth seeing.
    let current;
    if (device.currentSourceName) {
        current = h('span', {}, 'Showing ', h('span', { class: 'source-name', text: device.currentSourceName }));
    } else if (device.currentEncoderIp) {
        current = h('span', {}, 'Showing ', h('span', { class: 'source-name mono', text: device.currentEncoderIp }));
    } else if (device.onlineState === 'Offline') {
        current = h('span', { text: device.lastError || 'Not responding' });
    } else {
        current = h('span', { text: 'No source reported' });
    }

    // A destination carries its colour in one place only: the tick box, showing the encoder
    // routed to it. Grey means nothing has been routed here. Destinations have no edge bar -
    // two coloured marks on one row invited reading them as two different facts.
    //
    // Nothing here reacts to which source is selected. Selecting changes nothing on the wall,
    // so it changes nothing on screen.
    const classes = ['card', 'card-dest'];
    if (selected) classes.push('is-selected');
    if (device.onlineState === 'Offline') classes.push('is-offline');

    return h('button', {
        class: classes.join(' '),
        style: `--card-colour: ${sourceColour(device.routedSourceColorIndex)}`,
        onclick: () => {
            if (selected) state.selectedDestinations.delete(device.id);
            else state.selectedDestinations.add(device.id);
            renderRouting();
        }
    },
        h('span', { class: 'check', text: '✓' }),
        h('span', { class: `state-dot state-${device.onlineState}` }),
        h('span', { class: 'card-body' },
            h('div', { class: 'card-name', text: device.name }),
            h('div', { class: 'card-sub' }, current)));
}

function renderRouteBar() {
    const source = state.selectedSource ? byId(state.selectedSource) : null;
    const count = state.selectedDestinations.size;

    el.routeBtn.disabled = !source || count === 0;

    if (!source && count === 0) {
        el.routeSummary.textContent = 'Select a source and one or more destinations.';
    } else if (!source) {
        el.routeSummary.textContent = `${count} destination${count === 1 ? '' : 's'} selected. Now pick a source.`;
    } else if (count === 0) {
        el.routeSummary.replaceChildren(h('span', {}, h('strong', { text: source.name }), ' selected. Now pick destinations.'));
    } else {
        el.routeSummary.replaceChildren(h('span', {},
            'Route ', h('strong', { text: source.name }),
            ' to ', h('strong', { text: `${count} destination${count === 1 ? '' : 's'}` })));
    }

    el.routeBtn.textContent = count > 1 ? `Route to ${count}` : 'Route';
}

async function performRoute() {
    const source = byId(state.selectedSource);
    if (!source) return;

    const destinationIds = [...state.selectedDestinations];

    el.routeBtn.disabled = true;
    el.routeBtn.textContent = 'Routing…';

    log(`Routing ${source.name} to ${destinationIds.length} destination(s)`);

    try {
        const result = await api('POST', '/api/routes', {
            encoderId: source.id,
            decoderIds: destinationIds
        });

        renderRouteResults(result, source.colorIndex);

        for (const row of result.results) {
            log(`${row.decoder}: ${row.status}${row.error ? ' — ' + row.error : ''}`,
                row.status === 'Verified' ? 'ok' : 'fail');
        }

        // Clear the destinations once the route is done. Leaving them ticked means the next
        // route silently includes displays the operator finished with several routes ago.
        // The source stays selected, since sending one source to more displays is a normal
        // next step and re-picking it every time would be tedious.
        state.selectedDestinations.clear();
    } catch (error) {
        log(error.message, 'fail');
        el.routeResults.hidden = false;
        el.routeResults.replaceChildren(h('div', { class: 'warning', text: error.message }));
    } finally {
        // Redraw the cards, not just the button bar. The server pushes a device update as
        // soon as the route finishes, which can repaint the list before the selection is
        // cleared here - leaving ticks on screen that the state no longer holds.
        renderRouting();
    }
}

function renderRouteResults(result, colorIndex) {
    const rows = result.results.map(row => h('div', { class: 'result-row' },
        h('span', { class: `badge badge-${row.status}`, text: row.status }),
        h('span', { class: 'result-name', text: row.decoder }),
        h('span', { class: 'result-detail', text: row.error || (row.reportedEncoderIp ? 'following ' + row.reportedEncoderIp : '') })));

    const heading = result.failureCount === 0
        ? `${result.encoder} routed to ${result.successCount} destination${result.successCount === 1 ? '' : 's'}`
        : `${result.encoder}: ${result.successCount} verified, ${result.failureCount} failed`;

    // The results belong to one source, so the panel wears that source's colour.
    el.routeResults.style.setProperty('--card-colour', sourceColour(colorIndex));

    // replaceChildren stringifies anything that is not a node, so nulls have to be filtered
    // out rather than passed through the way h() tolerates them.
    const children = [
        h('h3', { text: heading }),
        result.warning ? h('div', { class: 'warning', text: result.warning }) : null,
        ...rows
    ].filter(Boolean);

    el.routeResults.hidden = false;
    el.routeResults.replaceChildren(...children);
}

// --- devices view -----------------------------------------------------------

function renderDevices() {
    renderDeviceTable(el.encoderTable, encoders(), false);
    renderDeviceTable(el.decoderTable, decoders(), true);
    renderDeviceTable(el.unknownTable, unclassified(), false);

    el.unclassifiedPanel.hidden = unclassified().length === 0;
}

function renderDeviceTable(table, devices, showSource) {
    const head = h('tr', {},
        h('th', { text: 'Name' }),
        h('th', { text: 'Address' }),
        h('th', { text: 'Model' }),
        showSource ? h('th', { text: 'Current source' }) : h('th', { text: 'Stream mode' }),
        h('th', { text: 'Status' }),
        h('th', {}));

    if (devices.length === 0) {
        table.replaceChildren(head, h('tr', {}, h('td', { colspan: '6' },
            h('div', { class: 'empty', text: 'Nothing here yet.' }))));
        return;
    }

    const rows = devices.map(device => h('tr', {},
        h('td', {}, h('div', { class: 'name-cell' },
            // Encoders carry their own swatch here so the Devices tab and the Routing tab
            // agree on which colour means which source.
            device.type === 'Encoder'
                ? h('span', { class: 'swatch', style: `--card-colour: ${sourceColour(device.colorIndex)}` })
                : null,
            h('input', {
                class: 'name-input',
                value: device.name,
                onchange: event => rename(device, event.target.value)
            }))),
        h('td', { class: 'mono', text: device.controlPort === 8000 ? device.ipAddress : `${device.ipAddress}:${device.controlPort}` }),
        h('td', { text: device.model || '—' }),
        showSource
            ? h('td', {},
                device.currentSourceName
                    ? h('span', {
                        class: 'swatch',
                        style: `--card-colour: ${sourceColour(device.routedSourceColorIndex)}`
                    })
                    : null,
                device.currentSourceName || device.currentEncoderIp || '—')
            : h('td', { text: device.streamMode || '—' }),
        h('td', {},
            h('span', { class: `state-dot state-${device.onlineState}` }),
            ' ', device.onlineState,
            device.lastError ? h('div', { class: 'device-error', text: device.lastError }) : null),
        h('td', {}, h('div', { class: 'cell-actions' },
            h('button', { class: 'btn btn-quiet', text: 'Probe', onclick: () => probe(device) }),
            h('button', { class: 'btn btn-quiet', text: 'Identify', onclick: () => identify(device) }),
            h('button', {
                class: 'btn btn-quiet',
                text: 'Web UI',
                onclick: () => window.open(`http://${device.ipAddress}`, '_blank', 'noopener')
            }),
            h('button', { class: 'btn btn-quiet btn-danger', text: 'Remove', onclick: () => remove(device) })))));

    table.replaceChildren(head, ...rows);
}

async function rename(device, name) {
    const trimmed = name.trim();
    if (!trimmed || trimmed === device.name) return;

    try {
        await api('PUT', `/api/devices/${device.id}`, { name: trimmed, ipAddress: device.ipAddress });
        log(`Renamed ${device.name} to ${trimmed}`);
    } catch (error) {
        log(error.message, 'fail');
    }
}

async function probe(device) {
    log(`Probing ${device.name} (${device.ipAddress})`);

    try {
        const result = await api('POST', `/api/devices/${device.id}/probe`);
        log(result.success ? `${device.name} responded: ${result.raw}` : `${device.name}: ${result.error}`,
            result.success ? 'ok' : 'fail');
    } catch (error) {
        log(error.message, 'fail');
    }
}

async function identify(device) {
    try {
        const result = await api('POST', `/api/devices/${device.id}/identify`);
        log(result.success
            ? `${device.name} should be flashing its front panel now`
            : `${device.name}: ${result.error}`,
            result.success ? 'ok' : 'fail');
    } catch (error) {
        log(error.message, 'fail');
    }
}

async function remove(device) {
    if (!confirm(`Remove ${device.name} from this application?\n\nThe endpoint itself is not changed.`)) {
        return;
    }

    try {
        await api('DELETE', `/api/devices/${device.id}`);
        state.selectedDestinations.delete(device.id);
        if (state.selectedSource === device.id) state.selectedSource = null;
        log(`Removed ${device.name}`);
    } catch (error) {
        log(error.message, 'fail');
    }
}

// --- diagnostics ------------------------------------------------------------

async function refreshStatus() {
    try {
        const status = await api('GET', '/api/system/status');

        const entries = [
            ['Version', status.version],
            ['Host', status.host],
            ['Web UI port', status.webPort],
            ['Reachable from network', status.listenOnAllInterfaces ? 'Yes' : 'No (localhost only)'],
            ['Endpoint control port', status.controlPort],
            ['Database', status.databasePath],
            ['Devices', `${status.deviceCount} (${status.encoderCount} encoders, ${status.decoderCount} decoders)`],
            ['Online', status.onlineCount],
            ['Offline', status.offlineCount]
        ];

        for (const nic of status.interfaces) {
            entries.push([nic.name, nic.address]);
        }

        el.statusGrid.replaceChildren(...entries.map(([label, value]) =>
            h('div', {}, h('dt', { text: label }), h('dd', { text: String(value) }))));
    } catch (error) {
        log(error.message, 'fail');
    }
}

// --- live state -------------------------------------------------------------

function connect() {
    const events = new EventSource('/api/events');

    events.addEventListener('open', () => {
        el.connection.className = 'connection is-live';
        el.connection.querySelector('.connection-label').textContent = 'live';
    });

    events.addEventListener('devices', event => {
        state.devices = JSON.parse(event.data);

        // Drop selections for devices that no longer exist, so a stale id cannot be routed.
        for (const id of [...state.selectedDestinations]) {
            if (!byId(id)) state.selectedDestinations.delete(id);
        }
        if (state.selectedSource && !byId(state.selectedSource)) {
            state.selectedSource = null;
        }

        renderRouting();
        renderDevices();
    });

    events.addEventListener('error', () => {
        el.connection.className = 'connection is-lost';
        el.connection.querySelector('.connection-label').textContent = 'reconnecting';
        // EventSource reconnects on its own; nothing to do but show it.
    });
}

// --- wiring -----------------------------------------------------------------

for (const tab of document.querySelectorAll('.tab')) {
    tab.addEventListener('click', () => {
        for (const other of document.querySelectorAll('.tab')) other.classList.remove('is-active');
        for (const view of document.querySelectorAll('.view')) view.classList.remove('is-active');

        tab.classList.add('is-active');
        document.getElementById('view-' + tab.dataset.view).classList.add('is-active');

        if (tab.dataset.view === 'diagnostics') refreshStatus();
    });
}

el.routeBtn.addEventListener('click', performRoute);

el.selectAll.addEventListener('click', () => {
    for (const device of decoders()) state.selectedDestinations.add(device.id);
    renderRouting();
});

el.selectNone.addEventListener('click', () => {
    state.selectedDestinations.clear();
    renderRouting();
});

el.addForm.addEventListener('submit', async event => {
    event.preventDefault();

    const ipAddress = document.getElementById('add-ip').value.trim();
    if (!ipAddress) return;

    const request = {
        name: document.getElementById('add-name').value.trim(),
        ipAddress,
        type: document.getElementById('add-type').value || null,
        controlPort: Number(document.getElementById('add-port').value) || 8000
    };

    try {
        const device = await api('POST', '/api/devices', request);

        log(device.onlineState === 'Online'
            ? `Added ${device.name} — ${device.model || 'model not reported'}, ${device.type}`
            : `Added ${device.name}, but it did not respond: ${device.lastError || 'no reply'}`,
            device.onlineState === 'Online' ? 'ok' : 'warn');

        el.addForm.reset();
        document.getElementById('add-port').value = 8000;
    } catch (error) {
        log(error.message, 'fail');
    }
});

document.getElementById('refresh-status').addEventListener('click', refreshStatus);
document.getElementById('clear-log').addEventListener('click', () => el.log.replaceChildren());

log('Visionary Router UI ready');
connect();
