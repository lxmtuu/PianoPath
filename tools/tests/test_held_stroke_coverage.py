"""Reference maths for the shader's pixel-box band integration; not a GPU/raster proof."""
import unittest
from pathlib import Path


def coverage(distance, width, pixel_half):
    pixel = max(pixel_half, 0.0001)
    overlap = min(distance + pixel, width) - max(distance - pixel, -width)
    return max(0, min(1, overlap / (2 * pixel)))


class HeldStrokeCoverageTests(unittest.TestCase):
    def test_reference_matches_the_shader_band_formula(self):
        shader=(Path(__file__).resolve().parents[2]/'Gpu/StageShaders.hlsl').read_text()
        body=shader.split('float HeldStrokeCoverage(',1)[1].split('}',1)[0]
        self.assertIn('float pixel = max(pixelHalfWidth, 0.0001);',body)
        self.assertIn('min(signedDistance + pixel, halfWidth) - max(signedDistance - pixel, -halfWidth)',body)
        self.assertIn('return saturate(overlap / (2.0 * pixel));',body)
        self.assertEqual(shader.count('return ShadeHeldParticleArt('),2)

    def test_subpixel_band_does_not_keep_half_opaque_card(self):
        for width in (0,0.001,0.01,0.1,0.25):
            self.assertAlmostEqual(coverage(0,width,0.5),2*width)

    def test_coverage_conserves_area_across_pixel_boundary(self):
        for width in (0.001,0.02,0.2,0.5,1.0,3.75):
            for phase in (0,0.1,0.49,0.5,0.99):
                self.assertAlmostEqual(sum(coverage(i-phase,width,0.5) for i in range(-12,13)),2*width,places=10)

    def test_symmetric_bounded_and_zero_outside_stroke(self):
        for width in (0.01,0.2,1):
            for distance in (0,0.1,0.5,1,2,8):
                a=coverage(distance,width,0.5)
                self.assertTrue(0<=a<=1)
                self.assertEqual(a,coverage(-distance,width,0.5))
                if distance>=width+0.5:self.assertEqual(a,0)
