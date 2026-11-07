/* Cozy Comments plugin: web client (jellyfin-web 10.11).
 * Injected into /web/index.html by the server plugin through File Transformation.
 * All behaviour is driven by GET /CozyComments/Settings (the plugin settings).
 * All user-supplied text is rendered with textContent, never innerHTML. */
(function () {
    'use strict';
    if (window.__jfComments) { return; }
    window.__jfComments = { loaded: Date.now() };

    var S = null;               // settings from the server (null until loaded)
    var settingsAt = 0;
    var settingsPromise = null;
    var itemTypeCache = {};
    var viewsCache = null;
    var scanQueued = false;
    var overlay = null;
    var SETTINGS_TTL = 60000;   // re-read settings at most once a minute

    // ---------- small helpers ----------
    function api() { return window.ApiClient; }
    function ready() { var a = api(); return !!(a && a.getCurrentUserId && a.getCurrentUserId() && a.accessToken && a.accessToken()); }
    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) { e.className = cls; }
        if (text !== undefined && text !== null) { e.textContent = String(text); }
        return e;
    }
    function headers(json) {
        var h = {};
        var a = api();
        if (a.setRequestHeaders) { a.setRequestHeaders(h); }
        if (!h.Authorization) { h.Authorization = 'MediaBrowser Token="' + a.accessToken() + '"'; }
        if (json) { h['Content-Type'] = 'application/json'; }
        h.Accept = 'application/json';
        return h;
    }
    function call(method, path, body, query) {
        var url = api().getUrl('CozyComments/' + path, query || undefined);
        return fetch(url, { method: method, headers: headers(body !== undefined), body: body !== undefined ? JSON.stringify(body) : undefined, credentials: 'same-origin' })
            .then(function (r) {
                if (r.status === 204) { return null; }
                return r.json().catch(function () { return {}; }).then(function (j) {
                    if (!r.ok) { var err = new Error((j && j.error) || ('Request failed (' + r.status + ')')); err.status = r.status; throw err; }
                    return j;
                });
            });
    }
    function hashParams() {
        var h = location.hash || '';
        var q = h.indexOf('?');
        return new URLSearchParams(q >= 0 ? h.slice(q + 1) : '');
    }
    function relTime(ms) {
        var diff = (ms - Date.now()) / 1000;
        var units = [['year', 31536000], ['month', 2592000], ['week', 604800], ['day', 86400], ['hour', 3600], ['minute', 60]];
        var fmt;
        try { fmt = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }); } catch (e) { fmt = null; }
        for (var i = 0; i < units.length; i++) {
            var v = diff / units[i][1];
            if (Math.abs(v) >= 1) { var n = Math.round(v); return fmt ? fmt.format(n, units[i][0]) : (Math.abs(n) + ' ' + units[i][0] + (Math.abs(n) === 1 ? '' : 's') + ' ago'); }
        }
        return 'just now';
    }

    // ---------- settings ----------
    function applyTheme() {
        var r = document.documentElement;
        r.style.setProperty('--jfc-accent', S.accentColor);
        r.style.setProperty('--jfc-star', S.starColor);
        r.style.setProperty('--jfc-page-bg', S.pageBackgroundColor);
        r.classList.toggle('jfc-collapse', !!S.collapseDetailSections);
    }
    function loadSettings(force) {
        if (!force && S && Date.now() - settingsAt < SETTINGS_TTL) { return Promise.resolve(S); }
        if (settingsPromise) { return settingsPromise; }
        settingsPromise = call('GET', 'Settings').then(function (s) {
            var changed = JSON.stringify(s) !== JSON.stringify(S);
            S = s; settingsAt = Date.now(); settingsPromise = null;
            applyTheme();
            if (changed) { refreshRendered(); }
            return S;
        }, function (e) { settingsPromise = null; throw e; });
        return settingsPromise;
    }
    // Settings changed (or a different user signed in): rebuild what we've drawn.
    function refreshRendered() {
        document.querySelectorAll('.jfc-section, .jfc-homecard').forEach(function (n) { n.remove(); });
        closeFeed();
    }
    function commentable(type) { return !!S && S.commentableTypes.indexOf(type) >= 0; }

    // ---------- pieces ----------
    function avatar(c) {
        var box = el('div', 'jfc-avatar');
        var letter = el('span', 'jfc-avatar-letter', (c.userName || '?').charAt(0).toUpperCase());
        box.appendChild(letter);
        if (c.imageTag) {
            var img = document.createElement('img');
            img.alt = '';
            img.loading = 'lazy';
            img.src = api().getUrl('Users/' + c.userId + '/Images/Primary', { tag: c.imageTag, height: 80, quality: 90 });
            img.onload = function () { letter.remove(); };
            img.onerror = function () { img.remove(); };
            box.appendChild(img);
        }
        return box;
    }
    function starsView(n) {
        var s = el('div', 'jfc-stars');
        s.setAttribute('role', 'img');
        s.setAttribute('aria-label', n + ' out of 5 stars');
        for (var i = 1; i <= 5; i++) { s.appendChild(el('span', i <= n ? 'jfc-star jfc-on' : 'jfc-star', '★')); }
        return s;
    }
    // Long comments: show the first previewWordLimit words (original line breaks kept), then "…" and a
    // "show more" button that expands in place. Client-side only; the API always returns the full text.
    function truncateAt(text, n) {
        // Returns the prefix ending at the n-th word, or null when the text has <= n words.
        var re = /\S+/g, m, count = 0, end = -1;
        while ((m = re.exec(text)) !== null) {
            count++;
            if (count === n) { end = re.lastIndex; }
            else if (count > n) { return text.slice(0, end); }
        }
        return null;
    }
    function fillText(t, text, spoiler) {
        var full = String(text == null ? '' : text);
        var limit = S.previewWordLimit || 0;
        var cut = limit > 0 ? truncateAt(full, limit) : null;
        if (cut === null) { t.textContent = full; return null; }
        t.textContent = '';
        t.appendChild(document.createTextNode(cut.replace(/[\s.,;:!?…]+$/, '') + '… '));
        var more = el('button', 'jfc-showmore', S.showMoreText);
        more.type = 'button';
        if (spoiler) { more.tabIndex = -1; } // not reachable while blurred; enabled on reveal
        more.addEventListener('click', function (e) {
            e.preventDefault(); e.stopPropagation();
            var hadFocus = document.activeElement === more;
            t.textContent = full;
            if (hadFocus) { t.tabIndex = -1; t.focus({ preventScroll: true }); }
        });
        t.appendChild(more);
        return more;
    }
    function textView(c) {
        var wrap = el('div', 'jfc-textwrap');
        var t = el('div', 'jfc-text');
        var more = fillText(t, c.text, !!c.spoiler);
        wrap.appendChild(t);
        if (c.spoiler) {
            wrap.classList.add('jfc-spoiler');
            var hint = el('button', 'jfc-spoilerhint', S.spoilerText);
            hint.type = 'button';
            wrap.appendChild(hint);
            var reveal = function (e) {
                e.preventDefault(); e.stopPropagation();
                wrap.classList.remove('jfc-spoiler'); wrap.classList.add('jfc-revealed'); hint.remove();
                wrap.removeEventListener('click', reveal, true);
                if (more) { more.removeAttribute('tabindex'); }
            };
            wrap.addEventListener('click', reveal, true);
        }
        return wrap;
    }
    function stopKeys(node) {
        // Keep Jellyfin's global keyboard shortcuts from reacting while typing.
        ['keydown', 'keyup', 'keypress'].forEach(function (k) { node.addEventListener(k, function (e) { e.stopPropagation(); }); });
    }

    // ---------- composer (new comment, reply, edit) ----------
    function composer(opts) {
        // opts: {placeholder, withStars, compact, initial:{text,stars,spoiler}, submitLabel, onSubmit(data)->Promise, onCancel}
        var MAX = S.maxCommentLength;
        var withStars = !!opts.withStars && S.starRatingsEnabled;
        var box = el('div', 'jfc-compose' + (opts.compact ? ' jfc-compact' : ''));
        var ta = document.createElement('textarea');
        ta.className = 'jfc-input';
        ta.maxLength = MAX;
        ta.rows = opts.compact ? 2 : 3;
        ta.placeholder = opts.placeholder || S.inputPlaceholder;
        ta.value = (opts.initial && opts.initial.text) || '';
        stopKeys(ta);
        box.appendChild(ta);

        var row = el('div', 'jfc-row');
        var stars = (opts.initial && opts.initial.stars) || 0;
        var picker = null;
        if (withStars) {
            picker = el('div', 'jfc-picker');
            picker.setAttribute('role', 'radiogroup');
            picker.setAttribute('aria-label', 'Rating (optional)');
            var paint = function (hover) {
                var v = hover || stars;
                Array.prototype.forEach.call(picker.children, function (b, i) { b.classList.toggle('jfc-on', i < v); b.setAttribute('aria-checked', String(i + 1 === stars)); });
            };
            for (var i = 1; i <= 5; i++) {
                (function (n) {
                    var b = el('button', 'jfc-star', '★');
                    b.type = 'button';
                    b.setAttribute('role', 'radio');
                    b.title = n + (n === 1 ? ' star' : ' stars') + ' (click again to clear)';
                    b.addEventListener('click', function () { stars = (stars === n) ? 0 : n; paint(); });
                    b.addEventListener('mouseenter', function () { paint(n); });
                    b.addEventListener('mouseleave', function () { paint(); });
                    picker.appendChild(b);
                })(i);
            }
            paint();
            row.appendChild(picker);
        }
        var cb = null;
        if (S.spoilersEnabled) {
            var spl = el('label', 'jfc-spoilerlabel');
            cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = !!(opts.initial && opts.initial.spoiler);
            spl.appendChild(cb);
            spl.appendChild(document.createTextNode(' Spoiler'));
            row.appendChild(spl);
        }
        var count = el('span', 'jfc-count', '');
        row.appendChild(count);
        row.appendChild(el('span', 'jfc-spacer'));
        var cancel = el('button', 'jfc-btn jfc-btn-flat', 'Cancel');
        cancel.type = 'button';
        var submit = el('button', 'jfc-btn jfc-btn-primary', opts.submitLabel || 'Comment');
        submit.type = 'button';
        row.appendChild(cancel);
        row.appendChild(submit);
        box.appendChild(row);
        var msg = el('div', 'jfc-msg');
        box.appendChild(msg);

        var update = function () {
            var len = ta.value.length;
            count.textContent = len > MAX - Math.min(200, Math.floor(MAX / 5)) ? (len + '/' + MAX) : '';
            submit.disabled = ta.value.trim().length === 0;
        };
        ta.addEventListener('input', update);
        update();
        cancel.addEventListener('click', function () {
            if (opts.onCancel) { opts.onCancel(); return; }
            ta.value = ''; stars = 0; if (cb) { cb.checked = false; }
            if (picker) { picker.querySelectorAll('.jfc-star').forEach(function (b) { b.classList.remove('jfc-on'); }); }
            msg.textContent = ''; update(); ta.blur();
        });
        submit.addEventListener('click', function () {
            if (submit.disabled) { return; }
            submit.disabled = true;
            msg.textContent = '';
            var initialSpoiler = !!(opts.initial && opts.initial.spoiler);
            opts.onSubmit({ text: ta.value, stars: withStars && stars ? stars : null, spoiler: cb ? cb.checked : initialSpoiler })
                .then(function () { ta.value = ''; stars = 0; if (cb) { cb.checked = false; } update(); })
                .catch(function (e) { msg.textContent = e.message || 'Something went wrong.'; submit.disabled = false; });
        });
        box.focusInput = function () { ta.focus(); };
        return box;
    }

    // ---------- item comments section ----------
    function renderComment(c, ctx, isReply) {
        var node = el('div', 'jfc-item' + (isReply ? ' jfc-reply' : ''));
        node.dataset.id = String(c.id);
        node.appendChild(avatar(c));
        var main = el('div', 'jfc-main');
        var head = el('div', 'jfc-head');
        head.appendChild(el('span', 'jfc-user', c.userName));
        var time = el('span', 'jfc-time', relTime(c.created));
        time.title = new Date(c.created).toLocaleString();
        head.appendChild(time);
        if (c.edited) { var ed = el('span', 'jfc-edited', '(edited)'); ed.title = new Date(c.edited).toLocaleString(); head.appendChild(ed); }
        main.appendChild(head);
        if (c.stars && S.starRatingsEnabled) { main.appendChild(starsView(c.stars)); }
        var body = textView(c);
        main.appendChild(body);

        var actions = el('div', 'jfc-actions');
        var replySlot = el('div', 'jfc-replyslot');
        if (S.repliesEnabled) {
            var rb = el('button', 'jfc-link', 'Reply');
            rb.type = 'button';
            rb.addEventListener('click', function () {
                if (replySlot.firstChild) { replySlot.textContent = ''; return; }
                var topId = isReply ? c.parentId : c.id;
                var comp = composer({
                    compact: true, withStars: false, placeholder: 'Reply to ' + c.userName + '…', submitLabel: 'Reply',
                    onCancel: function () { replySlot.textContent = ''; },
                    onSubmit: function (d) {
                        d.parentId = topId;
                        return call('POST', 'Items/' + ctx.itemId + '/Comments', d).then(function () { ctx.expanded[topId] = true; replySlot.textContent = ''; ctx.reload(); });
                    }
                });
                replySlot.appendChild(comp);
                comp.focusInput();
            });
            actions.appendChild(rb);
        }
        if (c.canEdit) {
            var eb = el('button', 'jfc-link', 'Edit');
            eb.type = 'button';
            eb.addEventListener('click', function () {
                var comp = composer({
                    compact: true, withStars: !isReply, initial: { text: c.text, stars: c.stars, spoiler: c.spoiler }, submitLabel: 'Save',
                    onCancel: function () { comp.replaceWith(body); actions.hidden = false; },
                    onSubmit: function (d) { return call('PUT', 'Comments/' + c.id, d).then(function () { ctx.reload(); }); }
                });
                body.replaceWith(comp);
                actions.hidden = true;
                comp.focusInput();
            });
            actions.appendChild(eb);
        }
        if (c.canDelete) {
            var db = el('button', 'jfc-link', 'Delete');
            db.type = 'button';
            db.addEventListener('click', function () {
                var q = (!isReply && c.replies && c.replies.length) ? 'Delete this comment and its ' + c.replies.length + ' repl' + (c.replies.length === 1 ? 'y' : 'ies') + '?' : 'Delete this comment?';
                if (!window.confirm(q)) { return; }
                call('DELETE', 'Comments/' + c.id).then(function () { ctx.reload(); }).catch(function (e) { window.alert(e.message); });
            });
            actions.appendChild(db);
        }
        if (actions.childNodes.length) { main.appendChild(actions); }
        main.appendChild(replySlot);

        if (!isReply && c.replies && c.replies.length) {
            var n = c.replies.length;
            var toggle = el('button', 'jfc-toggle', '');
            toggle.type = 'button';
            var list = el('div', 'jfc-replies');
            c.replies.forEach(function (r) { list.appendChild(renderComment(r, ctx, true)); });
            var setOpen = function (open) {
                list.hidden = !open;
                toggle.textContent = (open ? '▴ Hide ' : '▾ View ') + n + (n === 1 ? ' reply' : ' replies');
                if (open) { ctx.expanded[c.id] = true; } else { delete ctx.expanded[c.id]; }
            };
            toggle.addEventListener('click', function () { setOpen(list.hidden); });
            setOpen(!!ctx.expanded[c.id]);
            main.appendChild(toggle);
            main.appendChild(list);
        }
        node.appendChild(main);
        return node;
    }

    function buildSection(itemId) {
        var sec = el('div', 'verticalSection detailVerticalSection jfc-section');
        sec.dataset.jfcItem = itemId;
        var title = el('h2', 'sectionTitle sectionTitle-cards padded-right jfc-title', S.sectionTitle);
        var cnt = el('span', 'jfc-total', '');
        title.appendChild(cnt);
        sec.appendChild(title);
        var ctx = { itemId: itemId, expanded: {}, reload: null };
        var list = el('div', 'jfc-list');
        var comp = composer({
            withStars: true,
            onSubmit: function (d) { return call('POST', 'Items/' + itemId + '/Comments', d).then(function () { ctx.reload(); }); }
        });
        sec.appendChild(comp);
        sec.appendChild(list);
        ctx.reload = function () {
            return call('GET', 'Items/' + itemId + '/Comments').then(function (data) {
                list.textContent = '';
                cnt.textContent = data.total ? ' · ' + data.total : '';
                if (!data.comments.length) { list.appendChild(el('div', 'jfc-empty', S.emptyText)); }
                data.comments.forEach(function (c) { list.appendChild(renderComment(c, ctx, false)); });
            }).catch(function (e) { list.textContent = ''; list.appendChild(el('div', 'jfc-empty', 'Comments unavailable: ' + e.message)); });
        };
        ctx.reload();
        return sec;
    }

    function itemType(id) {
        if (itemTypeCache[id]) { return itemTypeCache[id]; }
        var a = api();
        itemTypeCache[id] = a.getItem(a.getCurrentUserId(), id).then(function (it) { return it && it.Type; }, function () { delete itemTypeCache[id]; return null; });
        return itemTypeCache[id];
    }

    // The section goes directly under the metadata block (genres / directors / writers / studios), inside the
    // primary content column, for movies, series (above Next Up and Seasons) and episodes. Fallback: before Cast & Crew.
    function anchorOf(page) {
        return page.querySelector('.detailPagePrimaryContent .itemDetailsGroup') ||
            (function () { var c = page.querySelector('.detailPageContent #castCollapsible'); return c ? c.previousElementSibling : null; })();
    }

    function scanDetails() {
        var pages = document.querySelectorAll('.itemDetailPage');
        var id = (hashParams().get('id') || '').replace(/-/g, '').toLowerCase();
        Array.prototype.forEach.call(pages, function (page) {
            var visible = !page.classList.contains('hide');
            var existing = page.querySelector('.jfc-section');
            if (!visible) { return; }
            if (S.collapseDetailSections) { try { scanCollapsibles(page); } catch (e) { console.warn('[Cozy Comments] collapse', e); } }
            if (!id || location.hash.indexOf('details') < 0) { if (existing) { existing.remove(); } return; }
            if (existing && existing.dataset.jfcItem === id) { return; }
            if (page.dataset.jfcPending === id) { return; }
            if (!anchorOf(page)) { return; }
            if (existing) { existing.remove(); }
            page.dataset.jfcPending = id;
            itemType(id).then(function (type) {
                delete page.dataset.jfcPending;
                var nowId = (hashParams().get('id') || '').replace(/-/g, '').toLowerCase();
                if (nowId !== id || page.classList.contains('hide') || !commentable(type)) { return; }
                var again = page.querySelector('.jfc-section');
                if (again && again.dataset.jfcItem === id) { return; }
                if (again) { again.remove(); }
                var a2 = anchorOf(page);
                if (a2 && a2.parentNode) { a2.parentNode.insertBefore(buildSection(id), a2.nextSibling); }
            });
        });
    }

    // ---------- optional: collapsible detail sections (+/-), collapsed on every page visit ----------
    var COLLAPSIBLE = ['#castCollapsible', '#guestCastCollapsible', '#specialsCollapsible',
        '#scenesCollapsible', '#additionalPartsCollapsible', '#musicVideosCollapsible'];
    var visit = 0; // bumped on every viewshow (= a page visit, incl. back/forward)
    function setCollapsed(sec, btn, collapsed) {
        sec.classList.toggle('jfc-col-open', !collapsed);
        btn.textContent = collapsed ? '+' : '−';
        btn.setAttribute('aria-expanded', String(!collapsed));
        var h = sec.querySelector(':scope > .sectionTitle');
        var name = h ? h.textContent.trim() : 'section';
        btn.setAttribute('aria-label', (collapsed ? 'Expand ' : 'Collapse ') + name);
        btn.title = collapsed ? 'Show' : 'Hide';
    }
    function scanCollapsibles(page) {
        var key = visit + ':' + ((hashParams().get('id') || '').toLowerCase());
        page.querySelectorAll(COLLAPSIBLE.join(',')).forEach(function (sec) {
            var h = sec.querySelector(':scope > h2.sectionTitle');
            if (!h) { return; }
            var btns = sec.querySelectorAll(':scope > .jfc-col-btn');
            for (var i = 1; i < btns.length; i++) { btns[i].remove(); }
            var btn = btns[0];
            if (!btn) {
                btn = el('button', 'jfc-col-btn', '+');
                btn.type = 'button';
                btn.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); setCollapsed(sec, btn, sec.classList.contains('jfc-col-open')); });
                // The header text also toggles (Jellyfin rewrites the h2's innerHTML, so the button lives beside it).
                h.addEventListener('click', function () { if (sec.querySelector(':scope > .jfc-col-btn') === btn) { btn.click(); } });
                sec.classList.add('jfc-col');
                h.insertAdjacentElement('afterend', btn);
                sec.dataset.jfcColKey = '';
            } else if (btn.previousElementSibling !== h) {
                h.insertAdjacentElement('afterend', btn);
            }
            if (sec.dataset.jfcColKey !== key) { sec.dataset.jfcColKey = key; setCollapsed(sec, btn, true); }
        });
    }

    // ---------- home screen: card in "My Media" that opens the comments page ----------
    function userViews() {
        if (viewsCache) { return viewsCache; }
        var a = api();
        viewsCache = a.getUserViews({}, a.getCurrentUserId()).then(function (r) { return (r && r.Items) || []; }, function () { viewsCache = null; return []; });
        return viewsCache;
    }

    function scanHome() {
        var home = document.querySelector('#indexPage.homePage:not(.hide)');
        if (!home) { return; }
        var libCards = home.querySelectorAll('.homeSectionsContainer .card[data-type="CollectionFolder"], .homeSectionsContainer .card[data-type="UserView"]');
        var buttons = home.querySelectorAll('.homeSectionsContainer .homeLibraryButton');
        if (!libCards.length && !buttons.length) { return; }
        var container = (libCards[0] || buttons[0]).parentNode;
        if (S.renameMyMediaTo) { renameMyMedia(container, S.renameMyMediaTo); }
        if (!S.commentsPageEnabled) { return; }
        if (container.querySelector('.jfc-homecard')) { return; }
        if (container.dataset.jfcBusy) { return; }
        container.dataset.jfcBusy = '1';
        userViews().then(function (views) {
            delete container.dataset.jfcBusy;
            if (!container.isConnected || container.querySelector('.jfc-homecard')) { return; }
            var after = (S.homeCardAfterLibrary || '').trim().toLowerCase();
            var vid = null;
            if (after) { views.forEach(function (v) { if ((v.Name || '').toLowerCase() === after) { vid = (v.Id || '').toLowerCase(); } }); }
            var cards = container.querySelectorAll(libCards.length ? '.card[data-type]' : '.homeLibraryButton');
            var anchor = null;
            if (after) {
                Array.prototype.forEach.call(cards, function (c) {
                    if (vid && (c.getAttribute('data-id') || '').toLowerCase() === vid) { anchor = c; }
                    var t = c.querySelector('.homeLibraryText');
                    if (t && t.textContent.trim().toLowerCase() === after) { anchor = c; }
                });
            }
            if (!anchor) { anchor = cards[cards.length - 1]; }
            if (!anchor) { return; }
            var card = libCards.length ? cloneCard(anchor) : cloneButton(anchor);
            var order = window.getComputedStyle(anchor).order;
            if (order && order !== '0') { card.style.order = order; }
            anchor.parentNode.insertBefore(card, anchor.nextSibling);
        });
    }

    // Optional: rename the heading of the section that holds the library tiles ("My Media"), in any UI language.
    function renameMyMedia(container, label) {
        for (var n = container; n && n !== document.body; n = n.parentElement) {
            for (var c = n.firstElementChild; c; c = c.nextElementSibling) {
                if (c.tagName === 'H2' && c.classList.contains('sectionTitle')) {
                    if (c.textContent !== label) { c.textContent = label; }
                    c.classList.add('jfc-hometitle');
                    return;
                }
            }
            if (n.classList && n.classList.contains('homeSectionsContainer')) { return; }
        }
    }

    function stripCard(root) {
        [root].concat(Array.prototype.slice.call(root.querySelectorAll('*'))).forEach(function (n) {
            Array.prototype.slice.call(n.attributes).forEach(function (a) { if (a.name.indexOf('data-') === 0) { n.removeAttribute(a.name); } });
            n.classList.remove('itemAction', 'lazy', 'blurhashed', 'lazy-image-fadein-fast', 'lazy-image-fadein');
        });
        root.querySelectorAll('canvas, .cardOverlayContainer, .cardIndicators, .indicators').forEach(function (n) { n.remove(); });
    }
    function openFromCard(e) { e.preventDefault(); e.stopPropagation(); openFeed(); }
    function cloneCard(src) {
        var c = src.cloneNode(true);
        stripCard(c);
        c.classList.add('jfc-homecard');
        c.setAttribute('title', S.pageName);
        var img = c.querySelector('.cardImageContainer');
        if (img) {
            img.removeAttribute('aria-label');
            img.classList.add('jfc-cardimg');
            img.textContent = '';
            if (S.coverTag) {
                img.classList.remove('defaultCardBackground');
                img.style.setProperty('background-image', 'url("../CozyComments/Cover?v=' + encodeURIComponent(S.coverTag) + '")', 'important');
            } else {
                img.style.backgroundImage = 'none';
                var icon = el('span', 'cardImageIcon material-icons jfc-cardicon', 'forum');
                icon.setAttribute('aria-hidden', 'true');
                img.appendChild(icon);
            }
            if (img.tagName === 'A') { img.setAttribute('href', '#'); }
        }
        c.querySelectorAll('a[href]').forEach(function (a) { a.setAttribute('href', '#'); });
        var texts = c.querySelectorAll('.cardText');
        if (texts.length) { texts[0].textContent = S.homeCardLabel; for (var i = 1; i < texts.length; i++) { texts[i].remove(); } }
        c.addEventListener('click', openFromCard, true);
        c.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { openFromCard(e); } }, true);
        return c;
    }
    function cloneButton(src) {
        var b = src.cloneNode(true);
        stripCard(b);
        b.classList.add('jfc-homecard');
        b.setAttribute('href', '#');
        var icon = b.querySelector('.homeLibraryIcon');
        if (icon) { icon.className = 'material-icons homeLibraryIcon'; icon.textContent = 'forum'; }
        var t = b.querySelector('.homeLibraryText');
        if (t) { t.textContent = S.homeCardLabel; }
        b.addEventListener('click', openFromCard, true);
        return b;
    }

    // ---------- the comments page (full-screen overlay with the newest comments) ----------
    function closeFeed() {
        if (!overlay) { return; }
        overlay.remove();
        overlay = null;
        document.documentElement.classList.remove('jfc-noscroll');
    }
    function openFeed() {
        closeFeed();
        if (!S || !S.commentsPageEnabled) { return; }
        overlay = el('div', 'jfc-overlay');
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-label', S.pageName);
        var inner = el('div', 'jfc-overlay-inner');
        var bar = el('div', 'jfc-overlay-bar');
        bar.appendChild(el('h2', 'sectionTitle jfc-overlay-title', S.pageName));
        var close = el('button', 'jfc-btn jfc-btn-flat jfc-close', 'Close');
        close.type = 'button';
        close.addEventListener('click', closeFeed);
        bar.appendChild(close);
        inner.appendChild(bar);
        var list = el('div', 'jfc-feed');
        inner.appendChild(list);
        var more = el('button', 'jfc-btn jfc-btn-flat jfc-more', 'Load more');
        more.type = 'button';
        more.hidden = true;
        inner.appendChild(more);
        overlay.appendChild(inner);
        document.body.appendChild(overlay);
        document.documentElement.classList.add('jfc-noscroll');
        close.focus();
        var next = 0;
        var serverId = api().serverId ? api().serverId() : '';
        var load = function () {
            more.disabled = true;
            call('GET', 'Recent', undefined, { startIndex: next, limit: S.pageSize }).then(function (d) {
                if (!d.items.length && next === 0) { list.appendChild(el('div', 'jfc-empty', S.emptyText)); }
                d.items.forEach(function (c) { list.appendChild(feedEntry(c, serverId)); });
                next += d.items.length;
                more.hidden = next >= d.totalRecordCount || !d.items.length;
                more.disabled = false;
            }).catch(function (e) { list.appendChild(el('div', 'jfc-empty', 'Could not load comments: ' + e.message)); more.disabled = false; });
        };
        more.addEventListener('click', load);
        load();
    }
    function feedEntry(c, serverId) {
        var node = el('div', 'jfc-item jfc-feeditem');
        node.appendChild(avatar(c));
        var main = el('div', 'jfc-main');
        var head = el('div', 'jfc-head');
        head.appendChild(el('span', 'jfc-user', c.userName));
        head.appendChild(el('span', 'jfc-on', c.parentId ? 'replied on' : 'on'));
        var a = el('a', 'jfc-itemlink', c.itemName);
        a.href = '#/details?id=' + encodeURIComponent(c.itemId) + (serverId ? '&serverId=' + encodeURIComponent(serverId) : '');
        a.addEventListener('click', function () { closeFeed(); });
        head.appendChild(a);
        var time = el('span', 'jfc-time', relTime(c.created));
        time.title = new Date(c.created).toLocaleString();
        head.appendChild(time);
        main.appendChild(head);
        if (c.stars && S.starRatingsEnabled) { main.appendChild(starsView(c.stars)); }
        main.appendChild(textView(c));
        node.appendChild(main);
        if (S.showPostersOnPage) { node.appendChild(feedPoster(c, a.href)); }
        return node;
    }
    // Poster thumbnail on the right of a row (series poster for episodes); placeholder icon when there is none.
    function feedPoster(c, href) {
        var link = el('a', 'jfc-poster');
        link.href = href;
        link.setAttribute('aria-hidden', 'true');
        link.tabIndex = -1;
        link.addEventListener('click', function () { closeFeed(); });
        var ph = el('span', 'material-icons jfc-poster-ph', c.itemType === 'Episode' || c.itemType === 'Series' ? 'tv' : 'movie');
        link.appendChild(ph);
        if (c.posterId && c.posterTag) {
            var img = document.createElement('img');
            img.alt = '';
            img.loading = 'lazy';
            img.decoding = 'async';
            img.src = api().getUrl('Items/' + c.posterId + '/Images/Primary', { tag: c.posterTag, maxHeight: 180, quality: 80 });
            img.onload = function () { ph.remove(); };
            img.onerror = function () { img.remove(); };
            link.appendChild(img);
        }
        return link;
    }

    // ---------- wiring ----------
    function scan() {
        scanQueued = false;
        if (!ready()) { return; }
        loadSettings(false).then(function () {
            try { scanDetails(); } catch (e) { console.warn('[Cozy Comments] details', e); }
            try { scanHome(); } catch (e) { console.warn('[Cozy Comments] home', e); }
        }, function (e) { console.warn('[Cozy Comments] settings', e); });
    }
    function queue() {
        if (scanQueued) { return; }
        scanQueued = true;
        setTimeout(scan, 120);
    }
    document.addEventListener('viewshow', function (e) {
        visit++;
        var t = e.target;
        if (S && S.collapseDetailSections && t && t.classList && t.classList.contains('itemDetailPage') && ready()) {
            try { scanCollapsibles(t); } catch (x) { /* next scan retries */ }
        }
        queue();
    }, true);
    window.addEventListener('hashchange', function () { closeFeed(); queue(); });
    window.addEventListener('popstate', function () { closeFeed(); queue(); });
    document.addEventListener('keydown', function (e) { if (overlay && e.key === 'Escape') { closeFeed(); } });
    // Reset per-user caches when the signed-in user changes.
    var lastUser = null;
    setInterval(function () {
        var a = api();
        var u = a && a.getCurrentUserId ? a.getCurrentUserId() : null;
        if (u !== lastUser) {
            lastUser = u; viewsCache = null; itemTypeCache = {}; S = null; settingsAt = 0;
            refreshRendered(); queue();
        }
    }, 1500);
    function observe() {
        if (!document.body) { return setTimeout(observe, 50); }
        new MutationObserver(queue).observe(document.body, { childList: true, subtree: true });
        queue();
    }
    observe();
})();
