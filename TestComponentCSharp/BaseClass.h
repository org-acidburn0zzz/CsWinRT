#pragma once
#include "BaseClass.g.h"

namespace winrt::TestComponentCSharp::implementation
{
    struct BaseClass : BaseClassT<BaseClass>
    {
        BaseClass() = default;

    };
}
namespace winrt::TestComponentCSharp::factory_implementation
{
    struct BaseClass : BaseClassT<BaseClass, implementation::BaseClass>
    {
    };
}
