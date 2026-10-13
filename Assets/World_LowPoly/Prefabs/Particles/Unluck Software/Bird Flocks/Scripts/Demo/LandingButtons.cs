using UnityEngine;

public class LandingButtons:MonoBehaviour{
    //Simple GUI script demo controlling bird flock and landing spots.
    
    public LandingSpotController _landingSpotController;
    public FlockController _flockController;
    public float hSliderValue = 100.0f;
	string label = "";

	float count;

	public int FPScount;
	public int samples = 100;
	public float totalTime;

	GUIStyle style;

	void Start()
	{
		style = new GUIStyle();
		style.fontSize = 20;
		style.normal.textColor = Color.black;
		
	}

	public void Update()
	{
		FPScount -= 1;
		totalTime += Time.deltaTime;

		if (FPScount <= 0)
		{
			float fps = samples / totalTime;
			label = "" + (int)fps;
			totalTime = 0f;
			FPScount = samples;
		}
	}

	public void OnGUI() {
		if (_flockController == null) return;
		

		GUI.Label(new Rect(5, 0, 100, 25), label, style);

		GUI.Label(new Rect(20.0f,20.0f,125.0f,18.0f),"Landing Spots: " + _landingSpotController.transform.childCount);
    	if(GUI.Button(new Rect(20.0f,40.0f,125.0f,18.0f),"Scare All"))
    		_landingSpotController.ScareAll();
    	if(GUI.Button(new Rect(20.0f,60.0f,125.0f,18.0f),"Land In Reach"))
    		_landingSpotController.LandAll();
    	if(GUI.Button(new Rect(20.0f,80.0f,125.0f,18.0f),"Land Instant"))
    		StartCoroutine(_landingSpotController.InstantLand(0.01f));
    	if(GUI.Button(new Rect(20.0f,100.0f,125.0f,18.0f),"Destroy")){
    		_flockController.destroyBirds();
    		}
    	GUI.Label(new Rect(20.0f,120.0f,125.0f,18.0f),"Bird Amount: " + _flockController._childAmount);
    	 _flockController._childAmount = (int)GUI.HorizontalSlider(new Rect (20.0f, 140.0f, 125.0f, 18.0f), (float)_flockController._childAmount, 0.0f , 2000.0f);
    }

	

}
