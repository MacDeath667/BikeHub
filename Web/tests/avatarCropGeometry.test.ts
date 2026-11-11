import { test } from "node:test";
import assert from "node:assert/strict";
import { centerCrop, cropImageStyle, moveCrop, zoomCrop } from "../src/components/avatarCropGeometry.ts";

test("initial selection is square and centered for landscape and portrait photos", () => {
  assert.deepEqual(centerCrop(1200, 800), { x: 200, y: 0, size: 800 });
  assert.deepEqual(centerCrop(800, 1200), { x: 0, y: 200, size: 800 });
  assert.deepEqual(centerCrop(512, 512), { x: 0, y: 0, size: 512 });
});
test("zoom preserves the selected center until image edges require clamping", () => {
  const crop = centerCrop(1200, 800);
  const zoomed = zoomCrop(crop, 1200, 800, 2);
  assert.deepEqual(zoomed, { x: 400, y: 200, size: 400 });
  assert.deepEqual(zoomCrop(zoomed, 1200, 800, 1), crop);
  assert.deepEqual(zoomCrop({ x: 0, y: 0, size: 400 }, 1200, 800, 1), { x: 0, y: 0, size: 800 });
});
test("dragging and zooming never expose empty pixels outside the photograph", () => {
  for (const [width, height] of [[1200, 800], [800, 1200], [10000, 2400], [40, 60]]) {
    for (const zoom of [0.5, 1, 1.05, 2, 4, 100]) {
      const scaled = zoomCrop(centerCrop(width, height), width, height, zoom);
      for (const [dx, dy] of [[-100000, -100000], [100000, 100000], [31, -47]]) {
        const crop = moveCrop(scaled, width, height, dx, dy);
        assert.ok(crop.x >= 0 && crop.y >= 0);
        assert.ok(crop.x + crop.size <= width && crop.y + crop.size <= height);
        assert.ok(crop.size >= Math.min(width, height) / 4 && crop.size <= Math.min(width, height));
      }
    }
  }
});
test("the circular preview and square selection show the same source region at every display size", () => {
  const crop = { x: 310, y: 170, size: 400 };
  for (const displaySize of [72, 100, 144, 320]) {
    const style = cropImageStyle(crop, 1200, 800, displaySize);
    const scale = style.width / 1200;
    assert.ok(Math.abs(-style.left / scale - crop.x) < 1e-9);
    assert.ok(Math.abs(-style.top / scale - crop.y) < 1e-9);
    assert.ok(Math.abs(displaySize / scale - crop.size) < 1e-9);
  }
});
