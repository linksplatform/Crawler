
#include "Common.h"
#include "Timestamp.h"

// see http://stackoverflow.com/questions/163058/how-can-i-detect-if-im-compiling-for-a-64bits-architecture-in-c
#if defined(_LP64) || defined(__amd64__) || defined(_M_X64)
#else
#warning "Links platform needs 64-bit CPU architecture."
#endif


int64_t LastTimestamp = 0;

// Получить число 100-наносекундных интервалов от 1 января 1601 года.

#if defined(_MFC_VER)
#include <windows.h>

int64_t GetTimestamp()
{

	FILETIME fileTime;

        // This structure is a 64-bit value representing the number of 100-nanosecond intervals since January 1, 1601.
	// The actual resolution depends on the system, http://msdn.microsoft.com/en-us/library/windows/desktop/ms724397%28v=vs.85%29.aspx

	GetSystemTimeAsFileTime(&fileTime);

	{
		int64_t = (((int64_t) fileTime.dwHighDateTime) << 32) | fileTime.dwLowDateTime;

		if (time <= LastTimestamp)
			return ++LastTimestamp;
		else
			return LastTimestamp = time;
	}
}

#elif defined(__GNUC__)

#include <stdint.h>
#include <time.h>

int64_t GetTimestamp()
{

#define DELTA_EPOCH_IN_MICROSECS  11644473600000000ULL

	// see 3.5 in http://citforum.ru/programming/c_unix/gl_3.shtml
	time_t t = time((time_t *)NULL);
	int64_t time = t;

	// see http://suacommunity.com/dictionary/gettimeofday-entry.php
	time += DELTA_EPOCH_IN_MICROSECS;
	time *= 10;

	if (time <= LastTimestamp)
		return ++LastTimestamp;
	else
		return LastTimestamp = time;
}
#endif
