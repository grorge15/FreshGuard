"""Bake a readable 0.24-second apple burst within a 1.3-second death (Pillow only)."""
from pathlib import Path
import json
import math
import random
from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Assets/AssetRaw/Actor/Sprites/Enemy/Frame/Apple"
DEST = ART / "DeathBurst"
SETTINGS = ROOT / "Tools/VFX/apple_death_import.json"
FPS, COUNT, MAX_SIZE = 50, 65, 384
HIT_STOP, BURST_TIME = .06, .10
EXPANSION_TIME, EXPANSION_MOTION = .24, .12
# Give the fracture and outgoing pieces twice as much screen time, keeping the
# initial impact and the 1.3-second recycling event at their existing times.
TAIL_SPEED = (.80-EXPANSION_MOTION)/(COUNT/FPS-BURST_TIME-EXPANSION_TIME)
RANDOM = random.Random(5621)
source = ImageOps.mirror(Image.open(ART / "Death/AppleDeath_02.png").convert("RGBA"))
bounds = source.getchannel("A").getbbox()
SOURCE_HEIGHT = bounds[3] - bounds[1]
BASE_HEIGHT = 280
BODY = source.crop(bounds)
BODY = BODY.resize((round(BODY.width * BASE_HEIGHT / BODY.height), BASE_HEIGHT), Image.Resampling.LANCZOS)


def clamp(v):
    return max(0., min(1., v))


def smooth(v):
    v = clamp(v)
    return v*v*(3-2*v)


def faded(im, opacity):
    result = im.copy()
    result.putalpha(result.getchannel("A").point(lambda a: round(a * clamp(opacity))))
    return result


def place(im, sprite, center, angle=0, opacity=1., scale=1.):
    if opacity <= 0:
        return
    if scale != 1:
        sprite = sprite.resize((max(1, round(sprite.width*scale)),max(1,round(sprite.height*scale))),Image.Resampling.LANCZOS)
    if angle:
        sprite = sprite.rotate(angle,resample=Image.Resampling.BICUBIC,expand=True)
    if opacity < 1:
        sprite = faded(sprite,opacity)
    im.alpha_composite(sprite,(round(center[0]-sprite.width/2),round(center[1]-sprite.height/2)))


def star(layer,center,radius,opacity,rotation=0):
    if opacity <= 0:
        return
    d=ImageDraw.Draw(layer)
    for factor,color in [(1.,(255,84,12)),(.73,(255,175,35)),(.40,(255,255,218))]:
        vertices=[]
        for i in range(24):
            angle=i*math.tau/24+rotation
            radius_i=radius*factor*(1+.13*math.sin(i*2.7) if i%2==0 else .45)
            vertices.append((center[0]+math.cos(angle)*radius_i,center[1]+math.sin(angle)*radius_i))
        d.polygon(vertices,fill=(*color,round(opacity*255)))


def glow(layer,center,radius,opacity):
    if opacity<=0:
        return
    halo=Image.new("RGBA",layer.size)
    d=ImageDraw.Draw(halo)
    d.ellipse((center[0]-radius,center[1]-radius,center[0]+radius,center[1]+radius),fill=(255,133,24,round(110*opacity)))
    layer.alpha_composite(halo.filter(ImageFilter.GaussianBlur(16)))


def body(im,sx=1,sy=1,lift=0,angle=0,flash=0):
    sprite=BODY.resize((round(BODY.width*sx),round(BODY.height*sy)),Image.Resampling.LANCZOS)
    if flash>0:
        lit=Image.new("RGBA",sprite.size,(255,246,196,255))
        lit.putalpha(sprite.getchannel("A"))
        sprite=Image.blend(sprite,lit,flash)
    center=(320,488-lift-sprite.height/2)
    place(im,sprite,center,angle)


def wedge(index):
    s=4
    im=Image.new("RGBA",(80*s,80*s))
    d=ImageDraw.Draw(im)
    points=[(12,19),(47,9),(67,25),(63,49),(34,68),(11,48)]
    points=[(x*s,y*s) for x,y in points]
    d.polygon(points,fill=(249,223,151),outline=(43,48,56),width=3*s)
    d.polygon([(12*s,19*s),(20*s,34*s),(18*s,46*s),(35*s,60*s),(62*s,44*s),(63*s,49*s),(34*s,68*s),(11*s,48*s)],fill=(220,52+index*3,61))
    d.line([(13*s,25*s),(15*s,46*s),(34*s,64*s),(60*s,47*s)],fill=(255,103,106),width=4*s)
    d.polygon([(22*s,23*s),(47*s,13*s),(62*s,26*s),(39*s,33*s)],fill=(255,243,193))
    if index%2==0:
        d.ellipse((36*s,30*s,43*s,42*s),fill=(115,68,43))
    return im.resize((80,80),Image.Resampling.LANCZOS)


def leaf():
    im=Image.new("RGBA",(120*3,68*3))
    d=ImageDraw.Draw(im)
    def pts(values):return [(x*3,y*3) for x,y in values]
    d.polygon(pts([(8,15),(39,5),(68,10),(101,33),(114,57),(82,60),(42,49),(18,32)]),fill=(43,49,56))
    d.polygon(pts([(15,18),(41,11),(67,16),(96,35),(103,50),(80,52),(44,43),(25,30)]),fill=(135,173,50))
    d.polygon(pts([(15,18),(41,11),(67,16),(96,35),(62,32),(34,26)]),fill=(189,213,84))
    d.polygon(pts([(34,26),(62,32),(103,50),(80,52),(44,43)]),fill=(103,142,42))
    return im.resize((90,51),Image.Resampling.LANCZOS)


LEAF=leaf()
CHUNKS=[wedge(i) for i in range(5)]
# Per-piece offsets and burst reach. Exponential drag spends most travel in the
# first 0.1 seconds, while gravity takes over for the short falling tail.
CHUNK_MOTION=[(-33,-2,-195,-125,76,-175),(-5,-34,-80,-185,70,220),
              (28,-15,155,-145,84,-220),(45,27,200,-55,61,240),(-21,35,-205,35,58,-260)]
DROPS=[(RANDOM.uniform(-235,235),RANDOM.uniform(-180,40),RANDOM.uniform(3.5,7.5),RANDOM.uniform(-30,30)) for _ in range(18)]
SPARKS=[(i*math.tau/12+RANDOM.uniform(-.13,.13),RANDOM.uniform(.8,1.2)) for i in range(12)]


def shadow(im,alpha=1,width=190):
    if alpha<=0:return
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    d.ellipse((320-width/2,478,320+width/2,502),fill=(6,10,16,round(175*alpha)))
    im.alpha_composite(layer.filter(ImageFilter.GaussianBlur(3)))


def effect_burst(im,age,center,rupture=False):
    duration = .22 if rupture else .10
    if not 0<=age<duration:return
    layer=Image.new("RGBA",im.size)
    p=age/duration
    glow(layer,center,(100 if rupture else 65)+35*p,(1-p)**2)
    flash_duration = .08 if rupture else .06
    if age<flash_duration:
        star(layer,center,(130 if rupture else 72)*(1+.25*p),1-age/flash_duration,age)
        if rupture:
            core=ImageDraw.Draw(layer)
            r=40*(1-age/flash_duration)
            core.ellipse((center[0]-r,center[1]-r,center[0]+r,center[1]+r),fill=(255,255,238,round(255*(1-age/flash_duration))))
    d=ImageDraw.Draw(layer)
    radius=30+(200 if rupture else 80)*(1-(1-p)**3)
    a=round(245*(1-p)**1.5)
    for start,end in [(8,80),(113,174),(201,270),(291,347)]:
        d.arc((center[0]-radius,center[1]-radius,center[0]+radius,center[1]+radius),start,end,fill=(255,190,64,a),width=max(1,round((9 if rupture else 4)*(1-p))))
    for angle,speed in SPARKS:
        radius=(30+(195 if rupture else 65)*(1-(1-p)**3))*speed
        length=(48 if rupture else 22)*(1-p)
        x=center[0]+math.cos(angle)*radius
        y=center[1]+math.sin(angle)*radius
        d.line((x-math.cos(angle)*length,y-math.sin(angle)*length,x,y),fill=(255,round(235-90*p),98,round(255*(1-p))),width=max(1,round((5 if rupture else 3)*(1-p))))
    im.alpha_composite(layer)


def chunk_y(age, oy, reach_y):
    return 333+oy+reach_y*(1-math.exp(-14*age))+850*age*age


def landing_time(oy, reach_y):
    lo, hi = 0., 1.
    for _ in range(24):
        mid = (lo+hi)/2
        if chunk_y(mid,oy,reach_y)<477:
            lo=mid
        else:
            hi=mid
    return hi


LANDINGS=[landing_time(oy,dy) for _,oy,_,dy,_,_ in CHUNK_MOTION]


def fragments(im,age):
    if age<0:return
    fade=1-smooth((age-.34)/.28)
    reach=1-math.exp(-14*age)
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    for vx,vy,size,jitter in DROPS:
        x=320+vx*reach
        y=333+jitter+vy*reach+740*age*age
        opacity=(1-smooth((age-.14)/.22))
        if y>493 or opacity<=0:continue
        stretch=1+2*math.exp(-age*12)
        d.ellipse((x-size*.6,y-size*stretch,x+size*.6,y+size*stretch),fill=(255,182,55,round(225*opacity)))
    if age<.24:
        p=age/.24
        for direction in [-1,1]:
            for i in range(3):
                x=320+direction*(30+90*(1-(1-p)**3)+i*19)
                r=(12+i*3)*(0.6+p)
                y=485-i*2-14*p
                d.ellipse((x-r,y-r*.55,x+r,y+r*.5),fill=(218,196,147,round(90*(1-p))))
    # A few brief streaks connect the fracture to the outgoing fruit pieces.
    if age<.12:
        for ox,oy,dx,dy,_,_ in CHUNK_MOTION:
            x,y=320+ox+dx*reach,chunk_y(age,oy,dy)
            trail=max(0,age-.025)
            tail_reach=1-math.exp(-14*trail)
            d.line((320+ox+dx*tail_reach,chunk_y(trail,oy,dy),x,y),
                   fill=(255,215,115,round(185*(1-age/.12))),width=5)
    im.alpha_composite(layer)
    for i,(ox,oy,dx,dy,size,spin) in enumerate(CHUNK_MOTION):
        x=320+ox+dx*reach
        y=chunk_y(age,oy,dy)
        if age>LANDINGS[i]:
            after=age-LANDINGS[i]
            y=min(477,477-110*after+1050*after*after)
        place(im,CHUNKS[i],(x,y),angle=spin*(reach+.35*age)+i*28,
              opacity=fade,scale=size/80*(1-.18*smooth((age-.40)/.22)))
    if .32<age<.62:
        flecks=Image.new("RGBA",im.size)
        d=ImageDraw.Draw(flecks)
        for i in range(12):
            p=(age-.32)/.30
            a=i*2.399
            x=320+math.cos(a)*(165+35*p)
            y=400+math.sin(a)*55-25*p
            r=max(.5,2.3*(1-p))
            d.ellipse((x-r,y-r,x+r,y+r),fill=(255,210,95,round(180*(1-p))))
        im.alpha_composite(flecks)
    if age<.78:
        x=330+42*math.sin(age*5)*(1+.3*age)
        y=235-70*(1-math.exp(-10*age))+410*age*age
        angle=25+math.sin(age*11)*38+age*100
        place(im,LEAF,(x,y),angle=angle,opacity=1-smooth((age-.54)/.22),scale=.9)


def render(elapsed):
    im = Image.new("RGBA", (640, 640))
    if elapsed < HIT_STOP:
        shadow(im)
        body(im, sx=1.08, sy=.92, flash=.93)
        # Hold the whole impact pose for three frames; only the dying visual stops.
        effect_burst(im,0,(328,343))
    elif elapsed < BURST_TIME:
        p = smooth((elapsed - HIT_STOP) / (BURST_TIME-HIT_STOP))
        shadow(im,1,205+20*p)
        body(im,sx=1.08+.20*p,sy=.92-.20*p,lift=10*p,angle=-5*p,flash=.45*(1-p))
        effect_burst(im,elapsed-HIT_STOP,(328,343))
    else:
        age=elapsed-BURST_TIME
        motion_age=(age*EXPANSION_MOTION/EXPANSION_TIME if age<EXPANSION_TIME
                    else EXPANSION_MOTION+(age-EXPANSION_TIME)*TAIL_SPEED)
        shadow(im,max(0,.45-motion_age*4),140)
        fragments(im,motion_age)
        effect_burst(im,motion_age,(320,333),True)
    # The prefab already mirrors its artwork. Export the opposite facing once.
    return ImageOps.mirror(im)


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    images = [render(i / FPS) for i in range(COUNT)]
    boxes = []
    for image in images:
        image.putalpha(image.getchannel("A").point(lambda a: a if a > 2 else 0))
        box = image.getchannel("A").getbbox()
        if box:
            boxes.append(box)
    crop = (max(0, min(b[0] for b in boxes) - 4),
            max(0, min(b[1] for b in boxes) - 4),
            min(640, max(b[2] for b in boxes) + 4),
            min(640, max(b[3] for b in boxes) + 4))
    factor = MAX_SIZE / max(crop[2] - crop[0], crop[3] - crop[1])
    scaled_size = (round((crop[2] - crop[0]) * factor), round((crop[3] - crop[1]) * factor))
    size = tuple((v + 3) // 4 * 4 for v in scaled_size)
    for i, image in enumerate(images):
        sprite = Image.new("RGBA", size)
        sprite.alpha_composite(image.crop(crop).resize(scaled_size, Image.Resampling.LANCZOS))
        sprite.save(DEST / f"AppleDeathBurst_{i:02}.png")
    data = {
        "frameCount": COUNT, "frameRate": FPS,
        "frameWidth": size[0], "frameHeight": size[1],
        "pivotX": (320 - crop[0]) * factor / size[0],
        "pivotY": (size[1] - (488 - crop[1]) * factor) / size[1],
        "pixelsPerUnit": 500 * BASE_HEIGHT / SOURCE_HEIGHT * factor,
        "completionTime": COUNT / FPS,
    }
    SETTINGS.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    assert not Image.open(DEST / f"AppleDeathBurst_{COUNT - 1:02}.png").getchannel("A").getbbox()
    print(json.dumps(data))


if __name__ == "__main__":
    main()

