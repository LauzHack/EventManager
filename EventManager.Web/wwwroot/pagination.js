/* Sets up client-side pagination + filtering for a list.
 *
 * Options:
 *   root               - CSS selector for the root item, so pagination buttons can be added above and below (required).
 *   itemsSelector      - CSS selector for the items to paginate, relative to the root (required).
 *   pageSize           - page size in items. Defaults to 50.
 *   searchSelector     - selector for an <input> that filters items by their `data-search` attribute (optional).
 *   emptyMessage       - text shown in the summary when the filter matches zero items.
 *   extraFilter(item)  - optional predicate run in addition to the text search;
 *                        items for which it returns false are excluded.
 *                        Call .recompute() on the returned handle when external state the predicate depends on changes.
 *
 * The page area always renders 7 items when totalPages > 7 to keep the left/right arrow positions stable across pages.
 *
 * Returns the `recompute` function so callers can trigger a refresh after external filter state changes.
 */
function setupPagination(options) {
    const opts = options || {};
    const root = document.querySelector(options.root);
    const items = [...document.querySelectorAll(options.root + " " + opts.itemsSelector)];
    const pageSize = opts.pageSize || 50;
    const search = opts.searchSelector ? document.querySelector(opts.searchSelector) : null;
    const emptyMessage = opts.emptyMessage || 'No matches.';
    const extraFilter = opts.extraFilter;

    const beforeRootSummary = document.createElement("p");
    beforeRootSummary.setAttribute("style", "margin: 0 auto; text-align: center;");
    const beforeRootButtons = document.createElement("nav");
    root.before(beforeRootSummary, beforeRootButtons);

    const afterRootSummary = document.createElement("p");
    afterRootSummary.setAttribute("style", "margin: 0 auto; text-align: center;");
    const afterRootButtons = document.createElement("nav");
    root.after(afterRootButtons, afterRootSummary);

    const summaries = [beforeRootSummary, afterRootSummary];
    const containers = [beforeRootButtons, afterRootButtons];
    const singleContainerThreshold = 11; // top container/summary won't be shown if there aren't at least this many items

    let currentPage = 1;

    function makeButton(label, page, btnOpts) {
        btnOpts = btnOpts || {};
        const b = document.createElement(btnOpts.current ? 'button' : 'a');
        b.textContent = label;
        if (btnOpts.disabled) b.style.visibility = 'hidden';
        b.addEventListener('click', () => {
            currentPage = page;
            recompute();
        });
        return b;
    }

    function makeEllipsis() {
        const span = document.createElement('span');
        span.textContent = '…';
        return span;
    }

    function pageItems(totalPages) {
        if (totalPages <= 7) {
            return Array.from({ length: totalPages }, (_, i) => ({ type: 'page', value: i + 1 }));
        }
        const items = [{ type: 'page', value: 1 }];
        if (currentPage <= 4) {
            items.push(
                { type: 'page', value: 2 },
                { type: 'page', value: 3 },
                { type: 'page', value: 4 },
                { type: 'page', value: 5 },
                { type: 'ellipsis' }
            );
        } else if (currentPage >= totalPages - 3) {
            items.push(
                { type: 'ellipsis' },
                { type: 'page', value: totalPages - 4 },
                { type: 'page', value: totalPages - 3 },
                { type: 'page', value: totalPages - 2 },
                { type: 'page', value: totalPages - 1 }
            );
        } else {
            items.push(
                { type: 'ellipsis' },
                { type: 'page', value: currentPage - 1 },
                { type: 'page', value: currentPage },
                { type: 'page', value: currentPage + 1 },
                { type: 'ellipsis' }
            );
        }
        items.push({ type: 'page', value: totalPages });
        return items;
    }

    function renderButtons(totalItems, totalPages) {
        var first = true;
        for (const container of containers) {
            container.innerHTML = '';
            if (totalPages <= 1 || (first && Math.min(pageSize, totalItems) < singleContainerThreshold)) {
                first = false;
                continue;
            }
            
            const leftContainer = document.createElement('ul');
            const leftItem = document.createElement('li');
            leftContainer.appendChild(leftItem);
            leftItem.appendChild(makeButton('‹', currentPage - 1, { disabled: currentPage === 1 }));
            container.appendChild(leftContainer);

            const pages = document.createElement('ul');
            for (const item of pageItems(totalPages)) {
                const itemContainer = document.createElement('li');
                if (item.type === 'page') {
                    itemContainer.appendChild(makeButton(String(item.value), item.value, { current: item.value === currentPage }));
                } else {
                    itemContainer.appendChild(makeEllipsis());
                }
                pages.appendChild(itemContainer);
            }
            container.appendChild(pages);

            const rightContainer = document.createElement('ul');
            const rightItem = document.createElement('li');
            rightContainer.appendChild(rightItem);
            rightItem.appendChild(makeButton('›', currentPage + 1, { disabled: currentPage === totalPages }));
            container.appendChild(rightContainer);
        }
    }

    function canonicalize(text) {
        // the goal here is to make searching easier, not to be "correct" in terms of pronunciation
        return text.replace('ß', 'ss').replace('ł', 'l').normalize("NFD").replace(/\p{Diacritic}/gu, "").trim().toLowerCase();
    }
    function split(text) {
        return text.match(/[^ ]+/g) || [];
    }
    function matches(text, query) {
        var queryParts = split(query);
        if (queryParts.length == 0) {
            return false;
        }
        for (const queryPart of queryParts) {
            if (split(text).find(n => n.startsWith(queryPart)) === undefined) {
                return false;
            }
        }
        return true;
    }

    function recompute() {
        const q = search ? canonicalize(search.value) : '';
        const matching = items.filter(r => {
            var data = canonicalize(r.dataset.search || '');
            if (q !== '' && !matches(data, q)) return false;
            return !(extraFilter && !extraFilter(r));
        });
        const totalPages = Math.max(1, Math.ceil(matching.length / pageSize));
        if (currentPage > totalPages) currentPage = totalPages;
        const start = (currentPage - 1) * pageSize;
        const end = Math.min(start + pageSize, matching.length);
        const visible = new Set(matching.slice(start, end));

        for (const item of items) {
            const show = visible.has(item);
            item.style.display = show ? '' : 'none';
        }

        const filtered = matching.length < items.length;
        var summaryText;
        if ((end - start) === items.length) {
            summaryText = "";
        } else if (matching.length > 0) {
            if (start + 1 == end) {
                summaryText = `Showing ${start + 1} of ${matching.length}` + (filtered ? ' matches' : '');
            } else {
                summaryText = `Showing ${start + 1}–${end} of ${matching.length}` + (filtered ? ' matches' : '');
            }
        } else {
            summaryText = emptyMessage;
        }
        var first = true;
        for (const s of summaries) {
            if (first && matching.length < singleContainerThreshold) {
                s.textContent = '';
                first = false;
            } else {
                s.textContent = summaryText;
            }
        }
        renderButtons(matching.length, totalPages);
    }

    if (search) {
        search.addEventListener('input', () => {
            currentPage = 1;
            recompute();
        });
    }
    recompute();

    return recompute;
}