/*
 * Checks that no <text> in the inline SVG diagrams overflows its box.
 *
 * The diagrams are hand-positioned SVG, so a translation that runs longer than the
 * English simply draws outside the rounded rectangle it belongs to — nothing clips it
 * and nothing scrolls. Devanagari and Cyrillic both run longer than English, so this is
 * a real risk on four of the five pages and impossible to see without measuring.
 *
 * Paste into the browser console on each language page, or run it through a headless
 * browser. Returns a list of problems; an empty list means everything fits.
 */
(function checkSvgFit() {
  const problems = [];

  document.querySelectorAll('.figure svg').forEach((svg, figureIndex) => {
    const viewBox = svg.getAttribute('viewBox').split(/\s+/).map(Number);
    const [, , vbWidth, vbHeight] = viewBox;

    // Every rounded rectangle is a box some text is meant to sit inside.
    const boxes = [...svg.querySelectorAll('rect')]
      .map(r => ({
        x: parseFloat(r.getAttribute('x')),
        y: parseFloat(r.getAttribute('y')),
        w: parseFloat(r.getAttribute('width')),
        h: parseFloat(r.getAttribute('height')),
      }))
      .filter(b => b.w > 60 && b.h > 40);   // ignore the thin progress tracks

    svg.querySelectorAll('text').forEach(text => {
      const b = text.getBBox();
      const label = text.textContent.trim();

      if (b.x < -1 || b.x + b.width > vbWidth + 1) {
        problems.push(
          `figure ${figureIndex}: "${label}" runs from ${Math.round(b.x)} to ` +
          `${Math.round(b.x + b.width)}, outside the ${vbWidth}-wide viewBox`);
        return;
      }

      if (b.y + b.height > vbHeight + 1) {
        problems.push(
          `figure ${figureIndex}: "${label}" bottom ${Math.round(b.y + b.height)} ` +
          `exceeds the ${vbHeight}-tall viewBox`);
      }

      // If the text starts inside a box, it has to end inside the same box.
      const box = boxes.find(k => b.x >= k.x && b.x <= k.x + k.w &&
                                  b.y >= k.y - 30 && b.y <= k.y + k.h);
      if (box && b.x + b.width > box.x + box.w - 6) {
        problems.push(
          `figure ${figureIndex}: "${label}" ends at ${Math.round(b.x + b.width)}, ` +
          `past its box which ends at ${Math.round(box.x + box.w)}`);
      }
    });
  });

  return problems;
})();
