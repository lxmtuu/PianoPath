import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from make_held_particle_clips import assemble,chunk,SIGNATURE
from review_held_particle_art import frames_from_apng
import struct
import zlib

class HeldReviewTests(unittest.TestCase):
    def test_all_sixty_lossless_frames_remain_ordered(self):
        def png(n):
            return SIGNATURE+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(bytes([0,n,0,0,255])))+chunk(b'IEND',b'')
        frames=[png(n) for n in range(60)]
        self.assertEqual(frames,frames_from_apng(assemble(frames,30)))
    def test_wrong_rate_or_count_rejected(self):
        png=SIGNATURE+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(bytes([0,0,0,0,255])))+chunk(b'IEND',b'')
        for frames,rate in [([png]*59,30),([png]*60,15)]:
            with self.assertRaises(ValueError):frames_from_apng(assemble(frames,rate))
