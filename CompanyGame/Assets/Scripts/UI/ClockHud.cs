using TMPro;
using UnityEngine;

public class ClockHud : MonoBehaviour
{
    public enum Weather { Clear, PartlyCloudy, Overcast, Rain, Snow }
    [SerializeField] Weather weather;
    TextMeshProUGUI date, time;
    readonly GameObject[] icons = new GameObject[6];
    static readonly string[] Days = { "월", "화", "수", "목", "금", "토", "일" };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { if (FindAnyObjectByType<ClockHud>()) return; var go = new GameObject("ClockHud"); DontDestroyOnLoad(go); go.AddComponent<ClockHud>(); }
    public void SetWeather(Weather value) => weather = value;
    void Awake()
    {
        var c = gameObject.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 100;
        var p = UITheme.Panel("ClockPanel", transform, new Vector2(340,112), UITheme.Hex(0x362B24,.83f),24,false);
        p.anchorMin = p.anchorMax = p.pivot = new Vector2(0, 1); p.anchoredPosition = new Vector2(16,-16);
        Shape(p,new Vector2(-72,0),new Vector2(2,66),UITheme.Hex(0xAA9177,.55f),1);
        date = Label(p,"서버 시간 연결 중",21,new Vector2(48,24),new Vector2(222,32));
        date.enableAutoSizing=true; date.fontSizeMin=14; date.fontSizeMax=21;
        time = Label(p,"--:--",42,new Vector2(48,-19),new Vector2(222,54));
        for(int i=0;i<6;i++) { var r=UIBuild.Rect("Weather"+i,p,new Vector2(76,76)); r.anchoredPosition=new Vector2(-118,0); icons[i]=r.gameObject; Icon(r,i); r.gameObject.SetActive(false); }
    }
    static TextMeshProUGUI Label(Transform p,string text,float size,Vector2 pos,Vector2 dimensions)
    { var t=UITheme.Text(p,text,size,Color.white,TMP_Settings.defaultFontAsset); t.rectTransform.anchoredPosition=pos;t.rectTransform.sizeDelta=dimensions;t.alignment=TextAlignmentOptions.MidlineLeft;t.fontStyle=FontStyles.Bold;return t; }
    static void Shape(Transform p,Vector2 pos,Vector2 size,Color color,float radius,float angle=0)
    { var r=UIBuild.Rect("IconPart",p,size);r.anchoredPosition=pos;r.localRotation=Quaternion.Euler(0,0,angle);UITheme.Rounded(r,color,radius,0); }
    static void Icon(Transform p,int state)
    {
        Color gold=UITheme.Hex(0xFFC66B);
        if(state<2) {
            Vector2 center=state==1?new Vector2(-12,12):Vector2.zero;
            Shape(p,center,new Vector2(34,34),gold,17);
            for(int j=0;j<8;j++){float a=j*Mathf.PI/4;Shape(p,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*27,new Vector2(9,4),gold,2,j*45);}
        }
        if(state>=1&&state<=4){
            Color cloud=UITheme.Hex(state==1?0xEEE3D4:0x9799A2);
            Shape(p,new Vector2(-17,1),new Vector2(28,28),cloud,14);
            Shape(p,new Vector2(0,10),new Vector2(36,36),cloud,18);
            Shape(p,new Vector2(20,0),new Vector2(28,28),cloud,14);
            Shape(p,new Vector2(1,-7),new Vector2(60,20),cloud,10);
            if(state>=3)for(int j=0;j<3;j++){
                Vector2 pos=new Vector2(-18+j*18,-27);
                if(state==3)Shape(p,pos,new Vector2(4,12),UITheme.Hex(0x9ACBEE),2,-22);
                else for(int k=0;k<3;k++)Shape(p,pos,new Vector2(11,2),Color.white,1,k*60);
            }
        }
        if(state==5)for(int j=0;j<28;j++){
            float a=(65+j*230f/27)*Mathf.Deg2Rad;float thickness=3+9*Mathf.Sin(j*Mathf.PI/27);
            Shape(p,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*23,new Vector2(thickness,thickness),UITheme.Hex(0xFFE3AA),thickness*.5f);
        }
    }
    void Update()
    {
        var clock=GameClock.Current;if(clock==null)return;
        var font=PlayerInteraction.Local?PlayerInteraction.Local.uiFont:null;if(font&&date.font!=font)date.font=time.font=font;
        var now=clock.Now;int icon=(int)weather;
        if(weather==Weather.Clear&&(now.hour<GameTime.DayStartHour||now.hour>=DayNightManager.DuskEnd))icon=5;
        for(int i=0;i<6;i++)if(icons[i].activeSelf!=(i==icon))icons[i].SetActive(i==icon);
        string d=$"{now.Year}년 {now.SeasonName} {now.DayOfSeason}일차 ({Days[now.absoluteDay%7]})";
        string t=$"{now.hour:00}:{now.minute:00}";
        if(date.text!=d)date.text=d;if(time.text!=t)time.text=t;
    }
}
