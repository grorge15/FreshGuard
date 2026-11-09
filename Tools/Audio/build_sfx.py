"""Rebuild synthesized sounds and the user's glass-break and robot-impact clips."""
from pathlib import Path
from io import BytesIO
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/AssetRaw/Audios/FreshGuardSfx'
RATE = 44100
rng = np.random.default_rng(20261109)

# id, module, label, trigger, level, cooldown, UI, recipe, notes
SOUNDS = [
 ('UiClick','通用界面','按钮点击','可操作按钮确认点击',.64,.045,True,'click','各界面共用；不可操作按钮不播放'),
 ('UiOpen','通用界面','弹窗打开','神秘商店、移除确认框、BUFF选择打开',.65,.10,True,'open','轻巧上扬'),
 ('UiClose','通用界面','弹窗关闭','关闭神秘商店或移除确认框',.55,.10,True,'close','轻巧下落'),
 ('Error','通用界面','操作失败','货币不足、达到上限、操作失败提示',.60,.35,True,'error','同类提示限频'),
 ('MatchFound','匹配','匹配成功','从匹配中切换到匹配成功',.85,.5,True,'match','每次匹配只播放一次'),
 ('Countdown','开战','准备倒计时','准备倒计时切换到新的正整数',.70,.15,True,'tick','随倒计时接近结束轻微升调'),
 ('BattleStart','开战','开战发球','双方能源球成功发射',.85,.5,False,'start','双方只播放一声'),
 ('LeverDown','老虎机商店','摇杆下压','刷新成功后摇杆开始下压',.78,.15,True,'lever','刷新失败不播放机械声'),
 ('LeverReturn','老虎机商店','摇杆复位','摇杆回弹复位',.56,.15,True,'return','与复位动画同步'),
 ('ReelSpin','老虎机商店','滚轴转动','滚轴开始转动，最后一个槽位停定时结束',.40,0,True,'spin','循环；隐藏、取消、终局时立即停止'),
 ('ReelStop','老虎机商店','槽位停定','三个商品槽依次停定',.78,.055,True,'stop','后两个槽位依次略微升调'),
 ('Purchase','老虎机商店','购买成功','玩家完成商品购买或购买参与的合并',.75,.10,True,'purchase','只表示交易成功；不添加机器人合成音效'),
 ('BallWall','能源球','撞墙反弹','己方能源球发生有效墙面反弹',.38,.075,False,'wall','仅己方棋盘；己方所有球共享限频'),
 ('GlassHit','玻璃','玻璃受击','己方玻璃实际扣耐久但尚未破碎',.40,.07,False,'glasshit','仅己方棋盘；恢复原版音色，音量由44%微调至40%'),
 ('GlassBreak','玻璃','普通玻璃破碎','己方普通玻璃耐久归零',.60,.075,False,'glassbreak','用户调整素材：玻璃破碎.MP3；仅己方；保留编辑后的短促碎裂声'),
 ('GlassRainbow','玻璃','彩色玻璃破碎','己方彩色玻璃耐久归零',.69,.09,False,'rainbow','用户调整素材：玻璃破碎.MP3；仅己方；使用同一声音，不额外叠加和弦'),
 ('CoinCollect','局内奖励','能源币到账','奖励飞行图标到达商店余额图标',.58,.075,True,'coin','仅玩家奖励；批量到账限频'),
 ('Combo','连击','Combo档位','现有Combo数字提示出现',.60,.12,False,'combo','根据档位升调；暴走触发时避免重复强调'),
 ('RageStart','暴走','进入暴走','玩家主球进入暴走状态',.86,.5,False,'rage','临时球共用这一次提示'),
 ('RageEnd','暴走','暴走结束','玩家暴走自然结束',.58,.5,False,'rageend','终局清理不播放结束声'),
 ('TowerHit','塔','塔受伤','己方塔实际扣血，与塔抖动同步',.73,.16,False,'tower','仅己方棋盘；不添加敌人攻击声音'),
 ('TowerDestroyed','塔','塔被摧毁','己方塔血量归零',.88,.5,False,'collapse','仅己方棋盘；通用胜负结算提示保留'),
 ('Victory','结算','胜利','胜利结算首次显示',.94,.5,True,'victory','完整短乐句，不循环'),
 ('Defeat','结算','失败','失败结算首次显示',.86,.5,True,'defeat','柔和下降，避免刺耳惩罚感'),
 ('RewardClaim','结算','奖励领取','奖励领取及翻倍成功；每局每个领取阶段一次',.82,.2,True,'reward','保存失败不播放；翻倍轻微升调'),
 ('BuffAcquired','神秘商店','获得助力','成功购买未持有的助力',.82,.15,True,'buff','购买成功后播放'),
 ('BuffStack','神秘商店','助力叠层','成功购买已持有助力并增加层数',.86,.15,True,'stack','比获得助力更明亮'),
 ('BuffRemove','神秘商店','移除助力','确认移除助力成功',.65,.15,True,'remove','取消操作只播放关闭声'),
 ('WaveAdvance','战斗流程','波次切换','已开战后波次增加',.52,.5,True,'wave','首波开战不重复提示'),
 ('BuffChoose','BUFF选择','选择强化','手动或超时完成BUFF三选一',.78,.15,True,'choose','已接界面；正常战斗尚未调用该界面'),
 ('RobotHit','机器人碰撞','撞击机器人','己方能源球首次有效接触机器人或在脱困端点反弹',.46,.09,False,'robothit','用户素材：刀打骷髅.MP3；仅己方；持续接触不重复播放'),
]

def tone(freq, duration, decay=8, kind='sine'):
    t = np.arange(round(duration * RATE)) / RATE
    phase = 2*np.pi*freq*t
    if kind == 'bell':
        x = np.sin(phase) + .28*np.sin(phase*2.76)*np.exp(-t*12) + .13*np.sin(phase*5.4)*np.exp(-t*23)
    else:
        x = np.sin(phase) + .12*np.sin(phase*2)
    return x*np.exp(-t*decay)*np.minimum(t/.003, 1)

def noise(duration, lo=1000, hi=9000, decay=15):
    t = np.arange(round(duration*RATE))/RATE
    f = np.fft.rfftfreq(len(t), 1/RATE)
    spectrum = np.fft.rfft(rng.normal(0,1,len(t)))
    band = (f/(lo+f))**2 * (hi/(hi+f))**4
    return np.fft.irfft(spectrum*band,n=len(t))*np.exp(-t*decay)*np.minimum(t/.001,1)

def sweep(a,b,duration):
    t = np.arange(round(duration*RATE))/RATE
    return np.sin(2*np.pi*(a*t+(b-a)*t*t/(2*duration)))*np.sin(np.pi*t/duration)**1.5

def mix(*parts):
    n = max(round(start*RATE)+len(x) for start,x in parts)
    y = np.zeros(n)
    for start,x in parts:
        k=round(start*RATE); y[k:k+len(x)]+=x
    return y

def notes(freqs, step=.09, duration=.42):
    return mix(*[(i*step,tone(f,duration,8,'bell')*.7) for i,f in enumerate(freqs)])

def glass(big=False):
    freqs = [1837, 2771, 3923, 5197, 7351]
    parts = [(i*.011, tone(f,.37 if big else .16,14 if big else 32,'bell')*(.42/(i+1))) for i,f in enumerate(freqs)]
    parts.append((0,noise(.48 if big else .13,2300,12500,14 if big else 40)*.50))
    if big:
        parts.extend((.08+i*.025,noise(.13,3300,14000,40)*.16) for i in range(6))
    return mix(*parts)

def user_robot_hit():
    # 此 WAV 为用户提供的「刀打骷髅.MP3」解码母版，原 MP3 同目录留存。
    with wave.open(str(ROOT/'Tools/Audio/Sources/robot_hit_user.wav'), 'rb') as w:
        if (w.getnchannels(), w.getsampwidth(), w.getframerate()) != (1, 2, RATE):
            raise ValueError('Robot source must be mono 16-bit PCM at 44100 Hz')
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float64)/32768

def user_glass_break():
    with wave.open(str(ROOT/'Tools/Audio/Sources/GlassBreak_User/glass_break_user.wav'), 'rb') as w:
        if (w.getnchannels(), w.getsampwidth(), w.getframerate()) != (1, 2, RATE):
            raise ValueError('User glass source must be mono 16-bit PCM at 44100 Hz')
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float64)/32768

def make(recipe):
    if recipe=='click': return mix((0,tone(1250,.085,65)*.5),(0,noise(.05,1600,8000,75)*.2))
    if recipe=='open': return mix((0,sweep(400,1400,.13)*.35),(.03,notes([784,1175],.055,.2)))
    if recipe=='close': return mix((0,sweep(1050,350,.12)*.4),(0,tone(660,.15,23)*.4))
    if recipe=='error': return notes([311,277],.095,.22)
    if recipe=='match': return notes([784,988,1175,1568],.085,.52)
    if recipe=='tick': return mix((0,tone(880,.12,30)),(0,noise(.035,2300,6000,70)*.08))
    if recipe=='start': return mix((0,notes([523,784,1047],.07,.47)),(0,sweep(220,1200,.22)*.22))
    if recipe in ('lever','return','stop'):
        f={'lever':210,'return':460,'stop':680}[recipe]
        return mix((0,noise(.10,450,4500,48)*.6),(0,tone(f,.17,27)*.7),(.028,noise(.07,1300,7500,60)*.3))
    if recipe=='spin':
        y=np.zeros(round(.18*RATE))
        for i in range(4):
            x=mix((0,noise(.033,800,4700,100)*.45),(0,tone(320+i*35,.033,90)*.18))
            k=round(i*.045*RATE); y[k:k+len(x)]+=x
        return y
    if recipe=='purchase': return notes([1047,1568],.065,.28)
    if recipe=='wall': return mix((0,tone(560,.095,45)*.8),(0,tone(1100,.06,65)*.16),(0,noise(.04,1300,6000,80)*.18))
    if recipe=='glasshit': return glass()
    if recipe in ('glassbreak','rainbow'):
        # 保持原合成器的随机推进次数，让本次替换不影响其他声音的波形。
        glass(True)
        return user_glass_break()
    if recipe=='robothit': return user_robot_hit()
    if recipe=='coin': return notes([1568,2093],.042,.18)
    if recipe=='combo': return notes([1175,1568],.055,.20)
    if recipe=='rage': return mix((0,sweep(180,2100,.37)*.55),(.12,notes([587,880,1175,1760],.055,.55)),(0,noise(.28,250,6000,8)*.18))
    if recipe=='rageend': return mix((0,sweep(1050,240,.28)*.32),(0,notes([1175,880,587],.07,.3)*.5))
    if recipe=='tower': return mix((0,sweep(170,60,.17)*.8),(0,noise(.22,180,3600,21)*.65),(0,tone(290,.16,30)*.3))
    if recipe=='collapse': return mix((0,sweep(170,37,.35)),(0,noise(.65,70,4200,7)*.7),(.16,noise(.4,400,6000,12)*.3))
    if recipe=='victory': return mix((0,notes([523,659,784,1047],.13,.62)),(.51,notes([784,1047,1319],.07,.7)*.7))
    if recipe=='defeat': return mix((0,notes([622,523,415,311],.19,.65)),(.60,tone(208,.65,5)*.2))
    if recipe=='reward': return notes([1047,1319,1568,2093],.055,.5)
    if recipe=='buff': return notes([880,1175,1480,1760],.075,.5)
    if recipe=='stack': return notes([1175,1480,1760,2349],.065,.55)
    if recipe=='remove': return notes([1047,784,523],.055,.25)
    if recipe=='wave': return notes([659,988],.095,.34)
    if recipe=='choose': return notes([988,1319,1760],.06,.4)
    raise ValueError(recipe)

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    entries=[]
    changed=[]
    for sound,module,label,trigger,volume,cooldown,ui,recipe,note in SOUNDS:
        x=make(recipe)
        user_glass=sound in ('GlassBreak','GlassRainbow')
        if not user_glass:
            x-=x.mean()
            edge=min(160,len(x)//8)
            x[:edge]*=np.linspace(0,1,edge); x[-edge:]*=np.linspace(1,0,edge)
            peak=np.max(np.abs(x)); x=x/peak*.68
        name='fg_'+''.join(('_'+c.lower()) if c.isupper() else c for c in sound).lstrip('_')
        buffer=BytesIO()
        with wave.open(buffer,'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
            pcm=np.rint(x*32768).astype('<i2') if user_glass else (x*32767).astype('<i2')
            w.writeframes(pcm.tobytes())
        path=OUT/(name+'.wav')
        payload=buffer.getvalue()
        if not path.exists() or path.read_bytes()!=payload:
            path.write_bytes(payload)
            changed.append(path.name)
        entry=dict(id=sound,module=module,label=label,trigger=trigger,location=name,volume=volume,cooldown=cooldown,ui=ui,loop=sound=='ReelSpin',duration=round(len(x)/RATE,3),source='原创程序合成，无第三方采样',note=note)
        if sound=='RobotHit':
            entry.update(source='用户提供：刀打骷髅.MP3',sourceFile='Tools/Audio/Sources/刀打骷髅.MP3',sourceProcessing='44.1 kHz单声道PCM转码、峰值匹配与首尾淡化；保留音高和时长')
        elif sound in ('GlassBreak','GlassRainbow'):
            entry.update(source='用户调整后提供：玻璃破碎.MP3',
                sourceFile='Tools/Audio/Sources/GlassBreak_User/玻璃破碎.MP3',
                sourceOriginalPath='F:/视频剪辑/玻璃破碎.MP3',
                sourceProcessing='仅44.1 kHz单声道PCM格式转换；保留音高、完整时长与素材音量；不额外叠加和弦')
        entries.append(entry)
    (OUT/'FreshGuardSoundCatalog.json').write_text(json.dumps({'version':1,'entries':entries},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'sounds':len(entries),'changed':changed,'bytes':sum(p.stat().st_size for p in OUT.glob('*.wav')),'output':str(OUT)},ensure_ascii=True))

if __name__=='__main__': main()
