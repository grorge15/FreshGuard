"""Bake the approved spinach, can and ghost-onion deaths as transparent sprites."""
from pathlib import Path
import argparse
import json
import math
import random
import re
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Assets/AssetRaw/Actor/Sprites/Enemy/Frame"
FPS, MAX_SIZE = 50, 384
SCALE = {"Spinach": .74, "FrozenCan": .80, "GhostOnion": .74}
PROFILES = [("Spinach", "SpinachEnemy", "SpinachEnemy", 55),
            ("FrozenCan", "ExpiredCanEnemy", "FrozenCanEnemy", 65),
            ("GhostOnion", "GhostOnionEnemy", "GhostOnionEnemy", 72)]
RNG = random.Random(13112026)
PALETTE = {"Spinach": ((207,231,88),(255,255,218)),
           "FrozenCan": ((107,210,250),(239,255,255)),
           "GhostOnion": ((184,155,238),(255,249,226))}
ARTWORK = {key: [Image.open(ART/key/f"Death/{key}Death_{i:02}.png").convert("RGBA")
                 for i in range(1,7)] for key,_,_,_ in PROFILES}

def clamp(x): return max(0., min(1., x))


def smooth(x):
    x=clamp(x)
    return x*x*(3-2*x)


def lerp(a,b,p): return a+(b-a)*p


def alpha(im,opacity):
    if opacity>=.999:return im
    im=im.copy()
    im.putalpha(im.getchannel("A").point(lambda a: round(a*clamp(opacity))))
    return im


def tint(im,strength,color=(255,250,214)):
    if strength<=0:return im
    lit=Image.new("RGBA",im.size,(*color,255))
    lit.putalpha(im.getchannel("A"))
    return Image.blend(im,lit,clamp(strength))


def paste(im,sprite,x,y,scale=1,angle=0,opacity=1,sx=1,sy=1):
    if opacity<=0:return
    size=(max(1,round(sprite.width*scale*sx)),max(1,round(sprite.height*scale*sy)))
    sprite=sprite.resize(size,Image.Resampling.LANCZOS)
    if angle:sprite=sprite.rotate(angle,Image.Resampling.BICUBIC,expand=True)
    sprite=alpha(sprite,opacity)
    im.alpha_composite(sprite,(round(x-sprite.width/2),round(y-sprite.height/2)))


def pose(im,sprite,key,dx=0,ground=490,lift=0,sx=1,sy=1,angle=0,flash=0,opacity=1,
         anchor=(256,450),grounded=False):
    scale=SCALE[key]
    a=math.radians(angle)
    cs,sn=math.cos(a),math.sin(a)
    center=((sprite.width/2-anchor[0])*scale*sx,(sprite.height/2-anchor[1])*scale*sy)
    rx=cs*center[0]+sn*center[1]
    ry=-sn*center[0]+cs*center[1]
    if grounded:
        bounds=sprite.getchannel("A").getbbox()
        offsets=[]
        for x,y in [(bounds[0],bounds[1]),(bounds[2],bounds[1]),(bounds[2],bounds[3]),(bounds[0],bounds[3])]:
            x=(x-anchor[0])*scale*sx
            y=(y-anchor[1])*scale*sy
            offsets.append(-sn*x+cs*y)
        ground-=max(offsets)
    paste(im,tint(sprite,flash),320+dx+rx,ground-lift+ry,scale,angle,opacity,sx,sy)


def shadow(im,x=320,width=185,opacity=1):
    if opacity<=0:return
    layer=Image.new("RGBA",im.size)
    ImageDraw.Draw(layer).ellipse((x-width/2,481,x+width/2,502),fill=(6,10,16,round(180*clamp(opacity))))
    im.alpha_composite(layer.filter(ImageFilter.GaussianBlur(3)))


def halo(im,x,y,radius,color,opacity):
    if opacity<=0:return
    layer=Image.new("RGBA",im.size)
    ImageDraw.Draw(layer).ellipse((x-radius,y-radius,x+radius,y+radius),fill=(*color,round(120*clamp(opacity))))
    im.alpha_composite(layer.filter(ImageFilter.GaussianBlur(15)))


def burst(im,age,key,center=(320,350),strength=1,ring=False):
    if not 0<=age<.28:return
    edge,core=PALETTE[key]
    p=age/.28
    layer=Image.new("RGBA",im.size)
    halo(layer,*center,55+55*p,edge,(1-p)**2*strength)
    d=ImageDraw.Draw(layer)
    if age<.14:
        radius=(27+46*min(1,age/.035))*strength
        for factor,color in [(1,edge),(.65,core),(.36,(255,255,249))]:
            pts=[]
            for i in range(24):
                a=i*math.tau/24+.13
                r=radius*factor*(1+.12*math.sin(i*2.4) if i%2==0 else .40)
                pts.append((center[0]+math.cos(a)*r,center[1]+math.sin(a)*r))
            d.polygon(pts,fill=(*color,round(255*(1-age/.14))))
    for i in range(11):
        a=i*math.tau/11+.08
        r=32+97*p
        x,y=center[0]+math.cos(a)*r,center[1]+math.sin(a)*r
        length=16*(1-p)
        d.line((x-math.cos(a)*length,y-math.sin(a)*length,x,y),fill=(*edge,round(230*(1-p))),width=max(1,round(3*(1-p))))
    if ring:
        r=20+95*p
        d.arc((center[0]-r,center[1]-r,center[0]+r,center[1]+r),8,177,fill=(*core,round(130*(1-p))),width=2)
        d.arc((center[0]-r,center[1]-r,center[0]+r,center[1]+r),204,337,fill=(*edge,round(180*(1-p))),width=3)
    im.alpha_composite(layer)


def masked_part(source,points):
    mask=Image.new("L",source.size)
    ImageDraw.Draw(mask).polygon(points,fill=255)
    result=Image.new("RGBA",source.size)
    result.paste(source,(0,0),mask)
    bounds=result.getchannel("A").getbbox()
    return result.crop(bounds),((bounds[0]+bounds[2])/2,(bounds[1]+bounds[3])/2)


SPINACH_POLYGONS=[
 [(172,258),(199,209),(260,169),(319,122),(387,95),(417,96),(438,123),(440,157),(423,176),(411,168),(414,145),(398,143),(384,157),(378,190),(374,214),(380,242),(360,280),(327,309),(286,332),(245,340),(223,365),(205,344),(196,301)],
 [(101,228),(111,183),(136,159),(163,141),(197,144),(218,164),(199,193),(172,226),(173,267),(141,269),(113,249)],
 [(346,292),(360,265),(388,253),(417,268),(430,297),(426,333),(405,343),(371,327)],
 [(60,337),(73,305),(105,276),(136,265),(167,277),(190,308),(206,352),(190,352),(163,312),(141,303),(123,326),(113,354),(87,366),(83,383),(66,385),(60,365)],
 [(248,366),(269,333),(304,306),(337,298),(364,306),(383,333),(381,358),(365,387),(343,400),(326,387),(339,369),(321,355),(315,334),(297,342),(271,366)]
]


LEAVES=[masked_part(ARTWORK["Spinach"][1],points) for points in SPINACH_POLYGONS]


ROOT_PART,ROOT_CENTER=masked_part(ARTWORK["Spinach"][1],[(155,433),(190,414),(185,362),(199,348),(218,399),(226,348),(243,333),(256,340),(244,400),(277,367),(288,374),(260,417),(281,423),(302,442),(280,451),(244,435),(213,435),(182,451),(158,446)])


LEAF_MOTION=[(130,-110,82),(-155,-126,-104),(184,-76,110),(-204,-60,-150),(150,-80,138)]


GREEN_DROPS=[(RNG.uniform(-190,190),RNG.uniform(-220,-70),RNG.uniform(2.5,5.5)) for _ in range(9)]


def spinach_frame(t):
    key="Spinach"
    im=Image.new("RGBA",(640,640))
    if t<0:
        shadow(im,width=193)
        pose(im,ARTWORK[key][0],key,lift=math.sin(t*4)*1.5)
        return im
    # The lethal-hit frame already contains separated leaves. Hold that broken
    # pose briefly, rather than holding an intact body before a delayed rupture.
    impact_time=max(0,t-.02)
    age=.06+impact_time*.75
    flash=.9*(1-smooth(impact_time/.06))
    fade=1-smooth((t-.68)/.34)
    shadow(im,width=210,opacity=max(0,1-impact_time/.84))
    collapse=smooth((.04+impact_time)/.14)
    root_height=1-.57*collapse
    root_y=490-(450-ROOT_CENTER[1])*SCALE[key]*root_height
    paste(im,tint(ROOT_PART,flash),320+(ROOT_CENTER[0]-256)*SCALE[key],root_y,SCALE[key],
          opacity=1-smooth((t-.60)/.28),sx=1+.13*collapse,sy=root_height)
    for i,((sprite,center),(vx,vy,spin)) in enumerate(zip(LEAVES,LEAF_MOTION)):
        release=max(0,age-i*.008)
        motion=release*1.4
        spread=1-math.exp(-3.2*motion)
        x=320+(center[0]-256)*SCALE[key]+vx*.68*spread+math.sin(motion*8+i)*9*motion
        y=490+(center[1]-450)*SCALE[key]+vy*motion+150*motion*motion
        flutter=.82+.18*math.cos(motion*7+i*.5)
        paste(im,tint(sprite,flash),x,y,SCALE[key],angle=spin*motion+math.sin(motion*7+i)*12*motion,
              opacity=fade,sx=flutter,sy=1-.1*motion)
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    for vx,vy,r in GREEN_DROPS:
        opacity=1-smooth((t-.38)/.26)
        if opacity<=0:continue
        x=320+vx*age
        y=414+vy*age+240*age*age
        if y<491:d.ellipse((x-r,y-r*1.5,x+r,y+r*1.5),fill=(175,205,61,round(205*opacity)))
    im.alpha_composite(layer)
    if impact_time<.10:
        burst(im,impact_time*.28/.10,key,(332,414),strength=.54)
    burst(im,impact_time*.28/.20,key,(336,335),strength=1.1,ring=True)
    return im


def bare_can():
    im=Image.new("RGBA",(512,512))
    d=ImageDraw.Draw(im)
    outline=(47,58,69)
    d.ellipse((109,371,403,423),fill=(114,146,164),outline=outline,width=7)
    d.rounded_rectangle((109,181,403,402),radius=24,fill=(176,99,112),outline=outline,width=7)
    d.rectangle((114,192,397,277),fill=(220,83,88))
    d.polygon([(114,199),(141,202),(143,390),(119,392)],fill=(192,68,76))
    d.polygon([(363,200),(399,192),(398,391),(366,401)],fill=(157,65,76))
    d.rounded_rectangle((144,288,366,387),radius=19,fill=(188,132,149))
    # Preserve the original food illustration from the label.
    meat,_=masked_part(ARTWORK["FrozenCan"][0],[(156,332),(165,320),(202,303),(243,300),(291,315),(313,329),(314,348),(299,361),(255,378),(218,380),(179,365),(159,352)])
    im.alpha_composite(meat,(156,300))
    d=ImageDraw.Draw(im)
    d.line((119,390,141,405,363,408,395,394),fill=(158,185,197),width=9)
    d.ellipse((105,144,405,218),fill=(154,183,201),outline=outline,width=7)
    d.ellipse((123,155,386,204),fill=(104,135,153),outline=(196,216,227),width=5)
    d.polygon([(269,161),(346,156),(378,169),(379,196),(262,197)],fill=(248,162,182))
    d.polygon([(269,161),(346,156),(378,169),(303,176)],fill=(255,193,207))
    d.line((172,231,198,243,174,248),fill=outline,width=8)
    d.line((308,230,282,243,308,250),fill=outline,width=8)
    d.line((223,266,239,260,253,270),fill=outline,width=6)
    return im


BARE_CAN=bare_can()


LID,LID_CENTER=masked_part(ARTWORK["FrozenCan"][0],[(218,197),(240,184),(256,158),(270,135),(293,121),(325,113),(347,111),(360,119),(362,131),(351,144),(316,164),(280,185),(250,197)])


def ice_piece(index):
    im=Image.new("RGBA",(120,140))
    d=ImageDraw.Draw(im)
    outer=[(24,12),(84,6),(112,61),(89,127),(28,133),(7,72)]
    d.polygon(outer,fill=(136,218,243),outline=(72,139,177),width=3)
    d.polygon([(24,12),(84,6),(65,63),(7,72)],fill=(215,251,255))
    d.polygon([(65,63),(112,61),(89,127),(49,107)],fill=(94,188,224))
    d.line([(25,17),(12,70),(30,126)],fill=(230,255,255),width=5)
    return im


ICE=[ice_piece(i) for i in range(8)]


ICE_MOTION=[(-105,-40,-215,-255,43),(-90,56,-204,-102,50),(87,5,245,-201,45),(72,78,190,-95,46),
            (-42,102,-85,-85,32),(110,52,258,-150,35),(-115,12,-239,-182,31),(15,100,53,-138,35)]


def crack_lines(im,opacity):
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    for pts in [[(207,330),(231,350),(211,374),(230,400),(209,427)],
                [(413,323),(386,348),(408,374),(386,393),(398,432)],
                [(222,429),(263,417),(281,437),(311,425),(340,443)]]:
        d.line(pts,fill=(52,116,157,round(opacity*230)),width=4)
        d.line([(x+2,y-1) for x,y in pts],fill=(235,255,255,round(opacity*255)),width=1)
    im.alpha_composite(layer)


def ice_fragments(im,age):
    if not 0<=age<.95:return
    opacity=1-smooth((age-.45)/.45)
    for i,(ox,oy,vx,vy,size) in enumerate(ICE_MOTION):
        x=320+ox+vx*age
        y=350+oy+vy*age+330*age*age
        if y>481:
            after=max(0,age-(-vy+math.sqrt(vy*vy+1320*(481-350-oy)))/660)
            y=481-60*after+330*after*after
        paste(im,ICE[i],x,y,size/140,angle=(i-4)*65*age,opacity=opacity)


def metal_lid(im,age):
    if not 0<=age<1.13:return
    vx,vy,g=128,-375,570
    y0=258
    land=(-vy+math.sqrt(vy*vy+4*g*(473-y0)))/(2*g)
    x=340+vx*age
    y=y0+vy*age+g*age*age
    if age>land:
        after=age-land
        y=473-130*after+570*after*after
        x=340+vx*(land+after*.4)
    y=min(483,y)
    paste(im,LID,x,y,.70,angle=-750*age,opacity=1-smooth((age-.85)/.25))


def ground_crash(im,age):
    if not 0<=age<.42:return
    p=age/.42
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    x=423
    for direction in [-1,1]:
        for i in range(4):
            cx=x+direction*(18+120*p+i*13)
            ry=(8+i*2)*(1+p)
            d.ellipse((cx-ry*1.3,489-ry,cx+ry*1.3,489+ry*.4),fill=(160,196,210,round(96*(1-p))))
    d.arc((x-35-100*p,480-9*p,x+35+100*p,498+9*p),180,354,fill=(198,238,250,round(220*(1-p))),width=max(1,round(4*(1-p))))
    if age<.10:
        d.line((368,486,389,473,408,485,429,466,449,486,474,476,494,488),fill=(235,250,255,round(255*(1-age/.1))),width=4)
    im.alpha_composite(layer)


def can_frame(t):
    key="FrozenCan"
    im=Image.new("RGBA",(640,640))
    if t<0:
        shadow(im,width=202)
        pose(im,ARTWORK[key][0],key)
        phase="待机"
    elif t<.08:
        shadow(im,width=202)
        pose(im,ARTWORK[key][1],key,dx=6,flash=.88)
        phase="击杀"
    elif t<.18:
        shadow(im,width=205)
        pose(im,ARTWORK[key][1],key,dx=8,sx=.98,sy=.97,angle=-3)
        crack_lines(im,smooth((t-.08)/.08))
        phase="冰裂"
    else:
        age=t-.18
        p=smooth(age/.42)
        angle=-87*p
        squash=1-.17*smooth((age-.40)/.09)
        bounce=10*math.sin(clamp((age-.42)/.15)*math.pi) if .42<age<.57 else 0
        shadow(im,x=320+100*p,width=180+27*p,opacity=1-smooth((t-.92)/.30))
        pose(im,BARE_CAN,key,dx=6,angle=angle,anchor=(256,421),grounded=True,lift=bounce,
             sx=squash,opacity=1-smooth((t-.95)/.28))
        phase="侧翻" if age<.42 else ("撞地" if age<.60 else ("消散" if t<1.25 else "完成"))
    if t>=.14:
        ice_fragments(im,t-.14)
        metal_lid(im,t-.14)
    ground_crash(im,t-.60)
    burst(im,t,key,(326,335),ring=True)
    if .14<=t<.30:burst(im,t-.14,key,(320,365),strength=.45)
    return im


def bezier(points,count=26):
    a,b,c,d=points
    out=[]
    for i in range(count):
        t=i/(count-1)
        q=1-t
        out.append((q*q*q*a[0]+3*q*q*t*b[0]+3*q*t*t*c[0]+t*t*t*d[0],
                    q*q*q*a[1]+3*q*q*t*b[1]+3*q*t*t*c[1]+t*t*t*d[1]))
    return out


def spirit_wisp(index):
    s=3
    im=Image.new("RGBA",(140*s,220*s))
    d=ImageDraw.Draw(im)
    outer=bezier([(72,208),(132,157),(6,121),(58,64)])+bezier([(58,64),(82,41),(113,41),(105,7)])
    inner=bezier([(105,7),(133,62),(61,72),(66,101)])+bezier([(66,101),(73,133),(145,163),(72,208)])
    points=[(x*s,y*s) for x,y in outer+inner]
    d.polygon(points,fill=(205,193,236,255))
    cream=bezier([(74,198),(110,158),(23,121),(65,67)])+bezier([(65,67),(87,45),(111,43),(105,7)])
    cream+=bezier([(105,7),(113,52),(65,65),(57,95)])+bezier([(57,95),(37,134),(114,161),(74,198)])
    d.polygon([(x*s,y*s) for x,y in cream],fill=(255,244,218,255))
    d.line([(x*s,y*s) for x,y in outer],fill=(169,151,205,240),width=2*s)
    return im.resize((140,220),Image.Resampling.LANCZOS)


WISPS=[spirit_wisp(i) for i in range(4)]


SPIRIT_DOTS=[(RNG.uniform(0,math.tau),RNG.uniform(20,105),RNG.uniform(.6,1.4)) for _ in range(14)]


def spirit_dissolve(im,age):
    if not 0<=age<1.02:return
    fade=1-smooth((age-.48)/.52)
    halo(im,320,337-70*age,43-12*age,(188,157,241),.7*(1-age))
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    for i in range(4):
        phase=i*math.tau/4
        a=phase+age*3.8
        radius=25+92*math.sin(min(1,age/.95)*math.pi*.83)
        x=320+math.cos(a)*radius
        y=327-100*age+math.sin(a)*radius*.39
        size=(.62 if i<3 else .78)*(1-.35*age)
        paste(im,WISPS[i],x,y,size,angle=math.degrees(-a)*.36+(i-1.5)*20,
              opacity=fade*(.92 if i<3 else .8),sx=.85+.15*math.cos(age*5+i))
    for i,(angle,radius,speed) in enumerate(SPIRIT_DOTS):
        x=320+math.cos(angle+age*1.8)*radius*(.65+.5*age)
        y=350+math.sin(angle)*radius*.3-age*130*speed
        r=2.6*(1-age)+.5
        opacity=fade*(.65+.35*math.sin(age*15+i)**2)
        d.ellipse((x-r,y-r,x+r,y+r),fill=(224,202,255,round(205*opacity)))
        if i%4==0:
            d.line((x-r*2,y,x+r*2,y),fill=(255,245,224,round(180*opacity)),width=1)
            d.line((x,y-r*2,x,y+r*2),fill=(255,245,224,round(180*opacity)),width=1)
    im.alpha_composite(layer)


def ghost_frame(t):
    key="GhostOnion"
    im=Image.new("RGBA",(640,640))
    if t<0:
        shadow(im,width=149,opacity=.58)
        pose(im,ARTWORK[key][0],key,lift=6+math.sin(t*4)*3)
        phase="待机"
    elif t<.08:
        shadow(im,width=149,opacity=.58)
        pose(im,ARTWORK[key][1],key,lift=6,dx=8,flash=.85)
        phase="击杀"
    elif t<.22:
        p=smooth((t-.08)/.14)
        shadow(im,width=149-18*p,opacity=.58-.10*p)
        pose(im,ARTWORK[key][1],key,dx=32*p,lift=6+8*p,sx=1+.17*p,sy=1-.08*p,angle=-10*p)
        pose(im,ARTWORK[key][1],key,dx=4*p,lift=6,opacity=.12*p,sx=1+.04*p)
        phase="拉扯"
    elif t<.44:
        p=smooth((t-.22)/.22)
        shadow(im,width=131*(1-p),opacity=.48*(1-p))
        # Collapse around the heart rather than flattening the spirit against the ground.
        sprite=tint(ARTWORK[key][1],p*.95,(255,249,232))
        center_y=339-35*p
        paste(im,sprite,320+32*(1-p),center_y,SCALE[key],angle=-10*(1-p),
              sx=1.17-.98*p,sy=.92-.36*p,opacity=1-.8*p)
        halo(im,320,326,15+32*math.sin(p*math.pi),(210,181,248),p)
        phase="收缩"
    else:
        phase="灵散" if t<1 else ("消散" if t<1.42 else "完成")
    burst(im,t,key,(329,335))
    if t>=.38:spirit_dissolve(im,t-.38)
    if .36<=t<.56:burst(im,t-.36,key,(320,311),strength=.48)
    return im



def bake(key, prefab, animation, count):
    renderer = {"Spinach": spinach_frame, "FrozenCan": can_frame, "GhostOnion": ghost_frame}[key]
    images = [renderer(i/FPS) for i in range(count)]
    boxes = []
    for image in images:
        image.putalpha(image.getchannel("A").point(lambda a: a if a > 2 else 0))
        box = image.getchannel("A").getbbox()
        if box:
            boxes.append(box)
    assert not images[-1].getchannel("A").getbbox(), key + " must disappear before recycling"
    crop = (max(0,min(b[0] for b in boxes)-4), max(0,min(b[1] for b in boxes)-4),
            min(640,max(b[2] for b in boxes)+4), min(640,max(b[3] for b in boxes)+4))
    factor = MAX_SIZE/max(crop[2]-crop[0],crop[3]-crop[1])
    scaled = (round((crop[2]-crop[0])*factor),round((crop[3]-crop[1])*factor))
    size = tuple((v+3)//4*4 for v in scaled)
    dest = ART/key/"DeathImpact"
    dest.mkdir(parents=True,exist_ok=True)
    for i,image in enumerate(images):
        sprite = Image.new("RGBA",size)
        sprite.alpha_composite(image.crop(crop).resize(scaled,Image.Resampling.LANCZOS))
        sprite.save(dest/f"{key}DeathImpact_{i:02}.png")
    # Match each original sprite's pivot and world size, including spinach's
    # slightly lower foot pivot. The prefab retains its authored flip and scale.
    meta = (ART/key/f"Death/{key}Death_02.png.meta").read_text(encoding="utf-8")
    pivot_y = float(re.search(r"spritePivot: \{x: [^,]+, y: ([^}]+)",meta)[1])
    ppu = float(re.search(r"spritePixelsToUnits: ([^\n]+)",meta)[1])
    anchor_y = 490+(ARTWORK[key][1].height*(1-pivot_y)-450)*SCALE[key]
    return {"key":key, "prefabName":prefab, "animationName":animation,
            "frameCount":count, "frameRate":FPS, "frameWidth":size[0], "frameHeight":size[1],
            "pivotX":(320-crop[0])*factor/size[0],
            "pivotY":(size[1]-(anchor_y-crop[1])*factor)/size[1],
            "pixelsPerUnit":ppu*SCALE[key]*factor,"completionTime":count/FPS}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--monster",choices=[profile[0] for profile in PROFILES])
    args = parser.parse_args()
    settings_path = ROOT/"Tools/VFX/monster_death_import.json"
    previous = {item["key"]:item for item in json.loads(settings_path.read_text())["monsters"]} if args.monster else {}
    settings = {"monsters":[bake(*profile) if args.monster in (None,profile[0]) else previous[profile[0]]
                            for profile in PROFILES]}
    settings_path.write_text(
        json.dumps(settings,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(settings))


if __name__ == "__main__":
    main()
