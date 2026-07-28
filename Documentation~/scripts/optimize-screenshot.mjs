import path from 'node:path';
import process from 'node:process';
import sharp from 'sharp';

const [source, destination, left, top, width, height] = process.argv.slice(2);
if (!source || !destination) {
  console.error('Usage: node scripts/optimize-screenshot.mjs <input> <output.webp> [left top width height]');
  process.exit(1);
}

let image = sharp(path.resolve(source));
if ([left, top, width, height].every((value) => value !== undefined)) {
  image = image.extract({
    left: Number(left),
    top: Number(top),
    width: Number(width),
    height: Number(height),
  });
}

await image
  .resize({width: 1600, withoutEnlargement: true})
  .webp({quality: 82, effort: 6})
  .toFile(path.resolve(destination));

console.log(`Optimized ${source} -> ${destination}`);
