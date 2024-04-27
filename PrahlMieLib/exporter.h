#pragma once

#if PRAHLMIELIB_EXPORTS
	#define API __declspec(dllexport)
#else
	#define API __declspec(dllimport)
#endif

extern "C" struct Complex {
	double Real;
	double Imagine;
};

extern "C" API void CalculateMie(
	double InSizeParameter,
	Complex InRefractionIndex,
	int InNumAngles,
	double* InAngleCosines,
	Complex * OutS1,
	Complex * OutS2,
	double* OutQext, double* OutQsca, double* OutQback, double* OutG
);