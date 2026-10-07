/* Behaviour of the plugins control panel on the user profile page (admin edit modal and logs viewer). */
(function () {
    'use strict';

    var modalEl = document.getElementById('pluginEditModal');
    if (!modalEl) return; // regular users have no edit modal

    function $(id) { return document.getElementById(id); }

    function toggleSections() {
        document.querySelector('.plugin-section-backend').classList.toggle('d-none', !$('pf-kind-backend').checked);
        document.querySelector('.plugin-section-ui').classList.toggle('d-none', !$('pf-kind-ui').checked);
    }

    function fill(state) {
        state = state || {};
        $('pf-id').value = state.id || 0;
        $('pf-key').value = state.key || '';
        $('pf-key').readOnly = !!state.id; // key is immutable once created
        $('pf-name').value = state.name || '';
        $('pf-version').value = state.version || '1.0.0';
        $('pf-description').value = state.description || '';
        $('pf-kind-backend').checked = state.kindBackend !== undefined ? state.kindBackend : true;
        $('pf-kind-ui').checked = !!state.kindUi;
        $('pf-enabled').checked = state.isEnabled !== undefined ? state.isEnabled : true;
        $('pf-default').checked = !!state.enabledByDefault;
        $('pf-script').value = state.script || '';
        $('pf-scope').value = state.eventScope || 0;
        $('pf-uihtml').value = state.uiHtml || '';
        $('pf-schema').value = state.configSchemaJson || '';

        var events = state.events || [], slots = state.slots || [];
        document.querySelectorAll('.pf-event').forEach(function (c) { c.checked = events.indexOf(c.value) >= 0; });
        document.querySelectorAll('.pf-slot').forEach(function (c) { c.checked = slots.indexOf(c.value) >= 0; });

        $('pluginEditTitle').innerHTML = '<i class="bi bi-plug me-2"></i>' + (state.id ? 'Edit plugin' : 'New plugin');
        toggleSections();
    }

    $('pf-kind-backend').addEventListener('change', toggleSections);
    $('pf-kind-ui').addEventListener('change', toggleSections);

    var newBtn = $('plugin-new-btn');
    if (newBtn) newBtn.addEventListener('click', function () { fill(null); });

    document.querySelectorAll('.plugin-edit-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            try { fill(JSON.parse(btn.getAttribute('data-plugin'))); } catch (e) { fill(null); }
        });
    });

    // Logs viewer
    var logsModal = $('pluginLogsModal');
    document.querySelectorAll('.plugin-logs-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var tbody = document.querySelector('#pluginLogsTable tbody');
            tbody.innerHTML = '';
            $('pluginLogsName').textContent = btn.getAttribute('data-plugin-name');
            $('pluginLogsEmpty').classList.add('d-none');
            fetch('?handler=PluginLogs&pluginId=' + encodeURIComponent(btn.getAttribute('data-plugin-id')), { credentials: 'same-origin' })
                .then(function (r) { return r.json(); })
                .then(function (rows) {
                    $('pluginLogsEmpty').classList.toggle('d-none', rows.length > 0);
                    rows.forEach(function (row) {
                        var tr = document.createElement('tr');
                        [row.time, row.level, row.trigger, row.userId == null ? '' : row.userId, row.message].forEach(function (value, i) {
                            var td = document.createElement('td');
                            td.textContent = value; // textContent: log messages are untrusted script output
                            if (i === 1) td.className = row.level === 'error' ? 'text-danger' : row.level === 'warn' ? 'text-warning' : '';
                            tr.appendChild(td);
                        });
                        tbody.appendChild(tr);
                    });
                })
                .catch(function () { $('pluginLogsEmpty').textContent = 'Could not load logs.'; $('pluginLogsEmpty').classList.remove('d-none'); });
        });
    });

    // After a failed save the server re-renders the page with the posted values: reopen the modal with them.
    var stateEl = $('plugin-form-state');
    if (stateEl) {
        try { fill(JSON.parse(stateEl.textContent)); } catch (e) { /* ignore */ }
        document.addEventListener('DOMContentLoaded', function () {
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        });
    } else {
        toggleSections();
    }
})();
