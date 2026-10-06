/*
 * Host side of the UI plugin bridge.
 *
 * UI plugins live in sandboxed iframes (opaque origin, no cookies, no DOM access to this page).
 * They talk to this script through postMessage; API calls are executed here with a short-lived,
 * scoped token that is issued for the signed-in user (see PluginUiController).
 *
 * Messages from the iframe:   hello | resize | api
 * Messages to the iframe:     init  | api-result
 */
(function () {
    'use strict';

    var frames = [];

    function afToken() {
        var el = document.getElementById('plugin-af');
        return el ? el.value : '';
    }

    function findRecord(source) {
        for (var i = 0; i < frames.length; i++) {
            if (frames[i].el.contentWindow === source) return frames[i];
        }
        return null;
    }

    function getToken(rec) {
        // Reuse the token until shortly before it expires.
        if (rec.token && Date.now() < rec.expires - 30000) return Promise.resolve(rec.token);
        return fetch('/plugins/' + encodeURIComponent(rec.pluginId) + '/ui-token', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'RequestVerificationToken': afToken() }
        }).then(function (r) {
            if (!r.ok) throw new Error('Could not get a plugin token (' + r.status + ').');
            return r.json();
        }).then(function (data) {
            rec.token = data.token;
            rec.expires = Date.now() + data.expiresIn * 1000;
            return rec.token;
        });
    }

    function reply(rec, message) {
        // The iframe has an opaque origin, so '*' is the only possible target origin; the receiver checks e.source.
        rec.el.contentWindow.postMessage(message, '*');
    }

    function handleApi(rec, m) {
        var method = String(m.method || 'GET').toUpperCase();
        if (['GET', 'POST', 'PUT', 'DELETE', 'PATCH'].indexOf(method) < 0)
            return Promise.reject(new Error('Unsupported method ' + method));

        var url;
        try { url = new URL(String(m.path), window.location.origin); }
        catch (e) { return Promise.reject(new Error('Invalid path')); }
        if (url.origin !== window.location.origin || url.pathname.indexOf('/api/') !== 0)
            return Promise.reject(new Error('Plugins may only call /api/ endpoints of this application.'));

        return getToken(rec).then(function (token) {
            var headers = { 'Authorization': token };
            var body;
            if (m.body !== undefined && m.body !== null && method !== 'GET' && method !== 'DELETE') {
                if (m.multipart && typeof m.body === 'object') {
                    var fd = new FormData();
                    Object.keys(m.body).forEach(function (k) {
                        var val = m.body[k];
                        if (val && val._isBlob) {
                            // Helper to pass base64 blobs from iframe
                            var byteCharacters = atob(val.base64);
                            var byteNumbers = new Array(byteCharacters.length);
                            for (var i = 0; i < byteCharacters.length; i++) byteNumbers[i] = byteCharacters.charCodeAt(i);
                            var byteArray = new Uint8Array(byteNumbers);
                            var blob = new Blob([byteArray], { type: val.type });
                            fd.append(k, blob, val.filename || 'file');
                        } else {
                            fd.append(k, val);
                        }
                    });
                    body = fd;
                } else if (m.form && typeof m.body === 'object') {
                    var params = new URLSearchParams();
                    Object.keys(m.body).forEach(function (k) { params.append(k, m.body[k]); });
                    body = params;
                } else {
                    headers['Content-Type'] = 'application/json';
                    body = JSON.stringify(m.body);
                }
            }
            return fetch(url.pathname + url.search, { method: method, headers: headers, body: body });
        }).then(function (r) {
            return r.text().then(function (text) {
                var parsed = text;
                try { parsed = text ? JSON.parse(text) : null; } catch (e) { /* keep text */ }
                return { status: r.status, ok: r.ok, body: parsed };
            });
        });
    }

    window.addEventListener('message', function (e) {
        var rec = findRecord(e.source);
        if (!rec || !e.data || typeof e.data !== 'object') return; // ignore anything that is not one of our iframes

        var m = e.data;
        if (m.type === 'hello') {
            reply(rec, Object.assign({ type: 'init' }, rec.init));
        } else if (m.type === 'resize') {
            var h = Math.max(40, Math.min(1500, Number(m.height) || 0));
            rec.el.style.height = h + 'px';
        } else if (m.type === 'api') {
            handleApi(rec, m).then(function (result) {
                reply(rec, { type: 'api-result', id: m.id, status: result.status, ok: result.ok, body: result.body });
            }).catch(function (err) {
                reply(rec, { type: 'api-result', id: m.id, error: err && err.message ? err.message : 'Request failed' });
            });
        }
    });

    function register() {
        document.querySelectorAll('iframe.plugin-frame').forEach(function (el) {
            var init = {};
            try { init = JSON.parse(el.getAttribute('data-init') || '{}'); } catch (e) { /* ignore */ }
            var rec = { el: el, pluginId: el.getAttribute('data-plugin-id'), init: init, token: null, expires: 0 };
            frames.push(rec);
            // Pro-actively initialize in case the iframe already sent 'hello' before we loaded
            reply(rec, Object.assign({ type: 'init' }, init));
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', register);
    else register();
})();
