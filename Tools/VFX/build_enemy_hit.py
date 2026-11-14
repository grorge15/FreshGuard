"""Rebuild the 16 transparent hit frames used in the approved apple hit GIF (Pillow)."""
from pathlib import Path
import math
import random
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "Assets/AssetRaw/Effects/EnemyHit"
SIZE, SCALE, COUNT, FPS = 256, 3, 16, 32
RNG = random.Random(5621293695)
SPIKES = [RNG.uniform(.72, 1.18) for _ in range(14)]
SPARKS = [(i * math.tau / 11 + RNG.uniform(-.12, .12), RNG.uniform(.78, 1.13), RNG.uniform(0, .055)) for i in range(11)]


def canvas(): return Image.new("RGBA", (SIZE * SCALE,) * 2)
def point(x,y): return (x*SCALE,y*SCALE)


def star(draw,radius,color,rotation=0,inner=.44):
    vertices=[]
    for i in range(28):
        angle=i*math.tau/28+rotation
        r=radius*(SPIKES[i//2] if i%2==0 else inner)
        vertices.append(point(128+math.cos(angle)*r,128+math.sin(angle)*r))
    draw.polygon(vertices,fill=color)


def frame(index):
    t=index/FPS
    result=canvas()
    strength=max(0,1-t/.30)**1.5
    if strength:
        glow=canvas()
        r=32+min(t/.10,1)*30
        ImageDraw.Draw(glow).ellipse((*point(128-r,128-r),*point(128+r,128+r)),fill=(255,66,3,round(120*strength)))
        result.alpha_composite(glow.filter(ImageFilter.GaussianBlur(14*SCALE)))
    d=ImageDraw.Draw(result)
    if t<.23:
        swell=min(1,.40+t/.045)
        fade=max(0,1-max(0,t-.06)/.17)
        radius=(48+22*min(t/.10,1))*swell
        star(d,radius,(255,58,6,round(255*fade)),-.21+t*.6)
        star(d,radius*.76,(255,139,14,round(255*fade)),-.13+t*.6,.50)
        star(d,radius*.51,(255,231,76,round(255*fade)),.07+t*.3,.58)
        if t<.135:star(d,radius*.34,(255,255,225,round(255*min(1,(.135-t)/.05))),-.10,.54)
    if t>=.055:
        p=min(1,(t-.055)/.39)
        r=35+65*(1-(1-p)**2)
        opacity=round(230*(1-p)**1.5)
        width=max(1,round((5.8*(1-p)+.6)*SCALE))
        box=(*point(128-r,128-r),*point(128+r,128+r))
        for start,end in [(4,88),(106,187),(204,276),(292,350)]:
            d.arc(box,start+p*10,end+p*10,fill=(255,round(176-82*p),23,opacity),width=width)
        if p<.45:d.arc(box,210,268,fill=(255,244,143,round(opacity*.85)),width=max(1,width//2))
    for angle,speed,delay in SPARKS:
        age=t-.045-delay
        if not 0<age<.37:continue
        p=age/.37
        distance=24+86*speed*(1-(1-p)**1.8)
        length=(17*(1-p)+3)*speed
        x,y=128+math.cos(angle)*distance,128+math.sin(angle)*distance+14*p*p
        ux,uy=math.cos(angle),math.sin(angle)
        width=2.5*(1-p)+.5
        vertices=[point(x+ux*length*.55,y+uy*length*.55),point(x-uy*width,y+ux*width),
                  point(x-ux*length,y-uy*length),point(x+uy*width,y-ux*width)]
        d.polygon(vertices,fill=(255,round(210-110*p),38,round(255*(1-p)**.8)))
        if p<.4:d.line([point(x-ux*length*.4,y-uy*length*.4),point(x+ux*length*.2,y+uy*length*.2)],
                       fill=(255,255,190,round(225*(1-p))),width=SCALE)
    return result.resize((SIZE,SIZE),Image.Resampling.LANCZOS)


if __name__=="__main__":
    DEST.mkdir(parents=True,exist_ok=True)
    for i in range(COUNT):frame(i).save(DEST/f"EnemyHit_{i:02}.png")
    print(f"Generated {COUNT} RGBA frames at {FPS} fps: {DEST}")
