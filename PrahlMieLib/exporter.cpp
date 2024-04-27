#include "exporter.h"

extern "C" {
#include "PrahlLib\mie_complex.h"
#include "PrahlLib\mie.h"
}

void CalculateMie(
	double InSizeParameter,
	Complex InRefractionIndex,
	int InNumAngles,
	double* InAngleCosines,
	Complex* OutS1,
	Complex* OutS2,
	double* OutQext, double* OutQsca, double* OutQback, double* OutG
)
{
	Mie(InSizeParameter, *(c_complex*)(&InRefractionIndex), InAngleCosines, InNumAngles, (c_complex*)OutS1, (c_complex*)OutS2, OutQext, OutQsca, OutQback, OutG);
}