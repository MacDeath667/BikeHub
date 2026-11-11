export type AvatarCrop = { x: number; y: number; size: number };
const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));

export function centerCrop(width: number, height: number): AvatarCrop {
  const size = Math.min(width, height);
  return { x: (width - size) / 2, y: (height - size) / 2, size };
}
export function moveCrop(crop: AvatarCrop, width: number, height: number, dx: number, dy: number): AvatarCrop {
  return { ...crop, x: clamp(crop.x + dx, 0, width - crop.size), y: clamp(crop.y + dy, 0, height - crop.size) };
}
export function zoomCrop(crop: AvatarCrop, width: number, height: number, zoom: number): AvatarCrop {
  const size = Math.min(width, height) / clamp(zoom, 1, 4);
  return { size, x: clamp(crop.x + (crop.size - size) / 2, 0, width - size), y: clamp(crop.y + (crop.size - size) / 2, 0, height - size) };
}
export function cropImageStyle(crop: AvatarCrop, width: number, height: number, displaySize: number) {
  const scale = displaySize / crop.size;
  return { width: width * scale, height: height * scale, left: -crop.x * scale, top: -crop.y * scale };
}
